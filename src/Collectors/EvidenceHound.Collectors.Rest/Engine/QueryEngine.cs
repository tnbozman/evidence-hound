using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// Collect phase: runs every query of a profile once, bootstrap first, as a dependency graph, and returns the snapshot. A query that
/// cannot be read completely is a result for that query, not a failed run: what depends on it fails naming it, and everything else runs.
/// </summary>
public sealed class QueryEngine(HttpClient http, IClock clock, IDelay delay)
{
    public async Task<Snapshot> CollectAsync(Profile profile, InstanceValues values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(values);
        var renderer = new Renderer(values.Parameters, new Dictionary<string, string>(StringComparer.Ordinal), values.Secrets);
        var allowed = profile.Connection.AllowedHosts.Select(h => renderer.Render(h).Match(v => v, _ => "")).Where(h => h.Length > 0).ToArray();
        var results = new Dictionary<string, QueryResult>(StringComparer.Ordinal);
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = QueryGraph.Order(profile) ?? [];
        var dependencies = QueryGraph.Dependencies(profile);
        var baseUrl = renderer.Render(profile.Connection.BaseUrl).Bind(BaseUrl);
        var sender = new RestHttp(http, clock, delay, profile.Connection, renderer, allowed);

        foreach (var query in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scoped = renderer.WithVars(vars);
            var result = Blocked(query, dependencies[query.Name], results)
                ?? await baseUrl.Match(
                    url => new QueryRun(query, scoped, url, sender, clock, delay).RunAsync(results, cancellationToken),
                    problems => Task.FromResult<QueryResult>(new QueryResult.Failed(query.Name, Describe(problems))));
            if (result is QueryResult.Collected collected)
            {
                result = SetVars(query, collected, vars);
            }

            results[query.Name] = result;
        }

        return new Snapshot(results, vars);
    }

    private static Result<Uri> BaseUrl(string text) =>
        Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var url)
            ? Result.Ok(url)
            : Result.Fail<Uri>(new Problem("rest.base_url_invalid", $"The base URL '{text}' is not an address."));

    /// <summary>A result for a query whose dependency did not give items, or null when it can run.</summary>
    private static QueryResult? Blocked(Query query, IReadOnlyList<string> dependsOn, Dictionary<string, QueryResult> results) =>
        dependsOn.Select(d => results.GetValueOrDefault(d)).FirstOrDefault(r => r is not QueryResult.Collected) switch
        {
            QueryResult.Unavailable unavailable => new QueryResult.Unavailable(query.Name, $"It depends on '{unavailable.Name}', which is not available: {unavailable.Reason}"),
            QueryResult.Failed failed => new QueryResult.Failed(query.Name, $"It depends on '{failed.Name}', which could not be read: {failed.Reason}"),
            _ => null,
        };

    /// <summary>Reads the query's variables from its first response; a variable that is not there fails the query.</summary>
    private static QueryResult SetVars(Query query, QueryResult.Collected collected, Dictionary<string, string> vars)
    {
        foreach (var (name, path) in query.Vars)
        {
            var value = JsonPath.Parse(path).Match(p => p.Select(collected.First), _ => PathMatch.Missing) switch
            {
                { Found: true, Values: [JsonValue scalar] } => Renderer.Text(scalar),
                _ => null,
            };
            if (value is null)
            {
                return new QueryResult.Failed(query.Name, $"Its answer has no single value at {path} for variable '{name}'.");
            }

            vars[name] = value;
        }

        return collected;
    }

    internal static string Describe(IEnumerable<Problem> problems) => string.Join(" ", problems.Select(p => p.Message));

    /// <summary>One query's run: one call, or one per item of another query, each followed through its pages.</summary>
    private sealed class QueryRun(Query query, Renderer renderer, Uri baseUrl, RestHttp sender, IClock clock, IDelay delay)
    {
        private int _pages;
        private long _bytes;
        private int _calls;
        private readonly JsonArray _items = [];
        private JsonNode? _first;

        public async Task<QueryResult> RunAsync(Dictionary<string, QueryResult> results, CancellationToken cancellationToken)
        {
            var targets = query.ForEach is { } forEach ? ((QueryResult.Collected)results[forEach]).Items.ToArray() : [null];
            if (targets.Length > query.MaxCalls)
            {
                return Fail(string.Create(CultureInfo.InvariantCulture, $"It would need {targets.Length} calls, one per item of '{query.ForEach}', more than its max_calls of {query.MaxCalls}."));
            }

            foreach (var item in targets)
            {
                var outcome = await CallAsync(item, cancellationToken);
                if (outcome is not null)
                {
                    return outcome;
                }
            }

            return new QueryResult.Collected(query.Name, _items, _pages, _calls, Request()) { Identities = query.Identities, IsBootstrap = query.IsBootstrap, First = _first };
        }

        private string Request() =>
            $"{query.Method} {renderer.Render(query.Path, mode: RenderMode.Evidence).Map(path => Uri.TryCreate(baseUrl, path, out var url) ? url.ToString() : path).Match(t => t, _ => query.Path)}";

        /// <summary>Runs one call (for one item) through its pages; null when it went well, else the query's result.</summary>
        private async Task<QueryResult?> CallAsync(JsonNode? item, CancellationToken cancellationToken)
        {
            _calls++;
            var first = FirstRequest(item);
            if (first is not Result<RestRequest>.Ok { Value: var request })
            {
                return Fail(Describe(((Result<RestRequest>.Fail)first).Errors));
            }

            if (query.Async is { } job)
            {
                var finished = await AwaitJobAsync(request, job, cancellationToken);
                if (finished is not Result<RestRequest>.Ok { Value: var download })
                {
                    return Fail(Describe(((Result<RestRequest>.Fail)finished).Errors));
                }

                request = download;
            }

            var pageNumber = 1;
            var offset = 0;
            for (var next = request; next is not null;)
            {
                if (_pages == query.MaxPages)
                {
                    return Fail(string.Create(CultureInfo.InvariantCulture, $"Paging stopped at its page cap of {query.MaxPages} pages, so the list may be incomplete."));
                }

                var sent = await sender.SendAsync(next, cancellationToken);
                if (sent is not Result<RestResponse>.Ok { Value: var response })
                {
                    return Fail(Describe(((Result<RestResponse>.Fail)sent).Errors));
                }

                if (!response.IsSuccess)
                {
                    return query.OptionalOn.Contains((int)response.Status)
                        ? new QueryResult.Unavailable(query.Name, string.Create(CultureInfo.InvariantCulture, $"{next.Url.Host} answered {(int)response.Status} ({response.Status}), which this profile reads as not available here."))
                        : Fail(string.Create(CultureInfo.InvariantCulture, $"{next.Url.Host} answered {(int)response.Status} ({response.Status})."));
                }

                _pages++;
                _bytes += query.Format == ResponseFormat.Zip ? response.Bytes.Length : response.Body.Length;
                if (_bytes > query.MaxBytes)
                {
                    return Fail(string.Create(CultureInfo.InvariantCulture, $"Its answers passed the byte cap of {query.MaxBytes} bytes; raise max_bytes or use keep."));
                }

                var tree = query.Format == ResponseFormat.Zip ? ZipMember.Read(response.Bytes, query.Member!, query.MaxBytes - _bytes, query.MaxBytes).Bind(text => Read(response with { Body = text }, ZipMember.FormatOf(query.Member!))) : Read(response, query.Format);
                if (tree is not Result<JsonBody>.Ok { Value.Node: var body })
                {
                    return Fail(Describe(((Result<JsonBody>.Fail)tree).Errors));
                }

                _first ??= body;
                var page = Items(body);
                if (page is null)
                {
                    return Fail($"Its answer has nothing at {query.Items}, where the items should be.");
                }

                foreach (var found in page)
                {
                    _items.Add(Keep(found));
                }

                if (_items.Count > query.MaxItems)
                {
                    return Fail(string.Create(CultureInfo.InvariantCulture, $"It returned more than its item cap of {query.MaxItems} items; raise max_items or narrow the request."));
                }

                offset += page.Count;
                pageNumber++;
                next = Next(next, response, body, page.Count, pageNumber, offset);
            }

            return null;
        }

        private QueryResult.Failed Fail(string reason) => new(query.Name, reason);

        private Result<RestRequest> FirstRequest(JsonNode? item) =>
            renderer.Render(query.Path, item, escapeItem: Uri.EscapeDataString).Bind(path => Address(path)).Bind(url =>
                Result.Combine(query.Parameters.Select(p => renderer.Render(p.Value, item).Map(v => KeyValuePair.Create(p.Key, v)))).Bind(parameters =>
                    Result.Combine(query.Headers.Select(h => renderer.Render(h.Value, item).Map(v => KeyValuePair.Create(h.Key, v)))).Bind(headers =>
                        renderer.RenderBody(query.Body, item).Map(body =>
                            new RestRequest(
                                query.Method == "POST" ? HttpMethod.Post : HttpMethod.Get,
                                FirstPage(parameters.Aggregate(url, (u, p) => QueryString.Set(u, p.Key, p.Value))),
                                headers.ToDictionary(StringComparer.OrdinalIgnoreCase),
                                body.Node)))));

        private Result<Uri> Address(string path) =>
            Uri.TryCreate(baseUrl, path, out var url) && url.IsAbsoluteUri
                ? Result.Ok(url)
                : Result.Fail<Uri>(new Problem("rest.address_invalid", $"'{path}' is not an address."));

        private Uri FirstPage(Uri url) => query.Paging switch
        {
            PageNumberPaging p => p.SizeParam is { } size
                ? QueryString.Set(QueryString.Set(url, p.Page, "1"), size, p.Size.ToString(CultureInfo.InvariantCulture))
                : QueryString.Set(url, p.Page, "1"),
            OffsetPaging o => QueryString.Set(QueryString.Set(url, o.OffsetParam, "0"), o.LimitParam, o.Limit.ToString(CultureInfo.InvariantCulture)),
            _ => url,
        };

        /// <summary>The next page's request, or null after the last page.</summary>
        private RestRequest? Next(RestRequest current, RestResponse response, JsonNode? body, int count, int pageNumber, int offset)
        {
            var url = query.Paging switch
            {
                NextLinkPaging n => Scalar(body, n.Next) is { Length: > 0 } link && Uri.TryCreate(baseUrl, link, out var next) ? next : null,
                PageNumberPaging p => int.TryParse(Scalar(body, p.TotalPages), NumberStyles.None, CultureInfo.InvariantCulture, out var total) && pageNumber <= total
                    ? QueryString.Set(current.Url, p.Page, pageNumber.ToString(CultureInfo.InvariantCulture))
                    : null,
                CursorPaging c => Scalar(body, c.Cursor) is { Length: > 0 } cursor ? QueryString.Set(current.Url, c.Param, cursor) : null,
                OffsetPaging o => count == 0 || (o.Total is { } totalPath
                        ? !int.TryParse(Scalar(body, totalPath), NumberStyles.None, CultureInfo.InvariantCulture, out var all) || offset >= all
                        : count < o.Limit)
                    ? null
                    : QueryString.Set(current.Url, o.OffsetParam, offset.ToString(CultureInfo.InvariantCulture)),
                LinkHeaderPaging => LinkHeader.Next(response.Header("Link")) is { } link && Uri.TryCreate(current.Url, link, out var next) ? next : null,
                _ => null,
            };
            return url is null ? null : current with { Url = url };
        }

        private static string? Scalar(JsonNode? body, string path) =>
            JsonPath.Parse(path).Match(p => p.Select(body), _ => PathMatch.Missing) switch
            {
                { Found: true, Values: [JsonValue value] } => Renderer.Text(value),
                _ => null,
            };

        private Result<JsonBody> Read(RestResponse response, ResponseFormat declared)
        {
            var format = declared switch
            {
                ResponseFormat.Auto when response.ContentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true => ResponseFormat.Xml,
                ResponseFormat.Auto when response.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true => ResponseFormat.Csv,
                ResponseFormat.Auto => ResponseFormat.Json,
                var explicitFormat => explicitFormat,
            };
            return format switch
            {
                ResponseFormat.Xml => XmlTree.Parse(response.Body, query.Arrays).Map(tree => new JsonBody(tree)),
                ResponseFormat.Csv => CsvTable.Parse(response.Body).Map(rows => new JsonBody(rows)),
                _ => Json(response.Body),
            };
        }

        private static Result<JsonBody> Json(string text)
        {
            try
            {
                return Result.Ok(new JsonBody(JsonNode.Parse(text)));
            }
            catch (JsonException ex)
            {
                return Result.Fail<JsonBody>(new Problem("rest.response_invalid", $"The answer is not JSON: {ex.Message}"));
            }
        }

        /// <summary>The page's items: the elements of the list at the items path, or the one object there.</summary>
        private List<JsonNode?>? Items(JsonNode? body) =>
            JsonPath.Parse(query.Items).Match(p => p.Select(body), _ => PathMatch.Missing) switch
            {
                { Found: false } => null,
                { Values: [JsonArray list] } => [.. list.Select(i => i?.DeepClone())],
                { Values: var values } when JsonPath.Parse(query.Items).Match(p => p.IsMulti, _ => false) => [.. values.Select(v => v?.DeepClone())],
                { Values: [var single] } => [single?.DeepClone()],
                _ => null,
            };

        private JsonNode? Keep(JsonNode? item) =>
            query.Keep.Count == 0 || item is not JsonObject obj
                ? item
                : new JsonObject(obj.Where(p => query.Keep.Contains(p.Key, StringComparer.Ordinal)).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));

        /// <summary>Starts a job, polls its status until done or out of time, and gives the request that downloads its result.</summary>
        private async Task<Result<RestRequest>> AwaitJobAsync(RestRequest start, AsyncJob job, CancellationToken cancellationToken)
        {
            var started = await sender.SendAsync(start, cancellationToken);
            if (started is not Result<RestResponse>.Ok { Value: var response })
            {
                return Result.Fail<RestRequest>(((Result<RestResponse>.Fail)started).Errors);
            }

            if (!response.IsSuccess)
            {
                return Result.Fail<RestRequest>(new Problem("rest.job_failed", string.Create(CultureInfo.InvariantCulture, $"{start.Url.Host} refused to start the job ({(int)response.Status}).")));
            }

            var body = Json(response.Body).Match(b => b.Node, _ => null);
            if (Scalar(body, job.StatusUrl) is not { Length: > 0 } status || !Uri.TryCreate(baseUrl, status, out var statusUrl))
            {
                return Result.Fail<RestRequest>(new Problem("rest.job_failed", $"The job's answer has no status address at {job.StatusUrl}."));
            }

            var deadline = clock.UtcNow + job.Timeout;
            while (true)
            {
                await delay.WaitAsync(job.Poll, cancellationToken);
                if (clock.UtcNow > deadline)
                {
                    return Result.Fail<RestRequest>(new Problem(
                        "rest.job_timeout", string.Create(CultureInfo.InvariantCulture, $"The job did not finish within {job.Timeout.TotalSeconds} seconds.")));
                }

                var polled = await sender.SendAsync(new RestRequest(HttpMethod.Get, statusUrl, start.Headers), cancellationToken);
                if (polled is not Result<RestResponse>.Ok { Value: { IsSuccess: true } state })
                {
                    return Result.Fail<RestRequest>(new Problem("rest.job_failed", "The job's status could not be read."));
                }

                var stateBody = Json(state.Body).Match(b => b.Node, _ => null);
                if (IsDone(job.Done, stateBody))
                {
                    return Scalar(stateBody, job.ResultUrl) is { Length: > 0 } result && Uri.TryCreate(baseUrl, result, out var resultUrl)
                        ? Result.Ok(new RestRequest(HttpMethod.Get, FirstPage(resultUrl), start.Headers))
                        : Result.Fail<RestRequest>(new Problem("rest.job_failed", $"The finished job has no result address at {job.ResultUrl}."));
                }
            }
        }

        private static bool IsDone(FieldTest done, JsonNode? body) =>
            Scalar(body, done.Field) is { } value && done.Operator switch
            {
                TestOperator.EqualTo or TestOperator.In => done.Operands.Contains(value, StringComparer.OrdinalIgnoreCase),
                TestOperator.NotEqualTo => !done.Operands.Contains(value, StringComparer.OrdinalIgnoreCase),
                _ => true,
            };
    }
}

/// <summary>Reads the <c>rel="next"</c> address from an HTTP <c>Link</c> header.</summary>
public static class LinkHeader
{
    public static string? Next(string? header) =>
        header?.Split(',')
            .Select(part => part.Split(';'))
            .Where(bits => bits.Skip(1).Any(b => b.Trim().Replace(" ", "", StringComparison.Ordinal) is "rel=\"next\"" or "rel=next"))
            .Select(bits => bits[0].Trim().TrimStart('<').TrimEnd('>'))
            .FirstOrDefault();
}

/// <summary>Reads one member of a zip archive as text, refusing it when it is larger than the bytes left under the query's cap.</summary>
public static class ZipMember
{
    public static Result<string> Read(ReadOnlyMemory<byte> archive, string member, long bytesLeft, long cap)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(archive.ToArray(), writable: false), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, member, StringComparison.Ordinal));
            if (entry is null)
            {
                return Result.Fail<string>(new Problem("rest.zip_member_missing", $"The archive has no {member}; it holds {string.Join(", ", zip.Entries.Select(e => e.FullName))}."));
            }

            if (entry.Length > bytesLeft)
            {
                return Result.Fail<string>(TooLarge(cap));
            }

            using var stream = entry.Open();
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            for (int read; (read = stream.Read(buffer)) > 0;)
            {
                if (limited.Length + read > bytesLeft)
                {
                    return Result.Fail<string>(TooLarge(cap));
                }

                limited.Write(buffer, 0, read);
            }

            return Result.Ok(Encoding.UTF8.GetString(limited.ToArray()));
        }
        catch (InvalidDataException ex)
        {
            return Result.Fail<string>(new Problem("rest.response_invalid", $"The answer is not a zip archive: {ex.Message}"));
        }
    }

    /// <summary>How a member is read, by its extension.</summary>
    public static ResponseFormat FormatOf(string member) => Path.GetExtension(member).ToLowerInvariant() switch
    {
        ".xml" => ResponseFormat.Xml,
        ".csv" => ResponseFormat.Csv,
        _ => ResponseFormat.Json,
    };

    private static Problem TooLarge(long cap) =>
        new("rest.response_too_large", string.Create(CultureInfo.InvariantCulture, $"Its answers passed the byte cap of {cap} bytes once unzipped; raise max_bytes or use keep."));
}
