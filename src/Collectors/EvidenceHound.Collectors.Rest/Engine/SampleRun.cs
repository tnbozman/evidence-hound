using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One answer of a sample run, cleaned: the request it answered, and its body as it is saved in the recording.</summary>
public sealed record SampledResponse(string Method, string Path, IReadOnlyDictionary<string, string> Query, int Status, string File, string Body)
{
    /// <summary>The body as JSON, for its shape; null when it is not JSON.</summary>
    public JsonNode? Json { get; init; }
}

/// <summary>Every answer a sample run read, in order, cleaned for saving.</summary>
public sealed record SampleRecording(IReadOnlyList<SampledResponse> Responses);

/// <summary>
/// Runs a drafted profile's queries once against the real API, read-only, and keeps every answer as a recording: identities (the
/// profile's <c>identities</c> fields) hashed, tokens replaced, the API's address as <c>{{api}}</c>. A recording that still holds a secret
/// is refused. <see cref="WriteTestCase"/> saves them as a profile test with the results the person confirmed.
/// </summary>
public static class SampleRun
{
    public const string RecordedToken = "recorded-token";

    private static readonly string[] TokenFields = ["access_token", "refresh_token", "id_token"];

    public static async Task<Result<SampleRecording>> RunAsync(Profile profile, InstanceValues values, HttpMessageHandler inner, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(values);
        var parameters = new Dictionary<string, string>(values.Parameters, StringComparer.Ordinal);
        foreach (var parameter in profile.Parameters.Where(p => p.Default is not null && !parameters.ContainsKey(p.Name)))
        {
            parameters[parameter.Name] = parameter.Default!;
        }

        using var recorder = new Recorder(inner);
        using var http = new HttpClient(recorder, disposeHandler: false);
        var snapshot = await new QueryEngine(http, clock, RealWait.Instance).CollectAsync(profile, values with { Parameters = parameters }, cancellationToken).ConfigureAwait(false);
        var failed = snapshot.Queries.Values.OfType<QueryResult.Failed>().ToArray();
        if (failed.Length > 0)
        {
            return Result.Fail<SampleRecording>(new Problem(
                "profile.sample_failed", $"The sample run could not read {string.Join("; ", failed.Select(f => $"{f.Name}: {f.Reason}"))}. Nothing was kept."));
        }

        var identities = profile.Queries.SelectMany(q => q.Identities).Select(i => i.Split('.')[^1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var secrets = values.Secrets.Values.ToArray();
        var responses = new List<SampledResponse>();
        foreach (var (exchange, index) in recorder.Exchanges.Select((e, i) => (e, i + 1)))
        {
            var cleaned = Clean(exchange.Body, exchange.Authorities, identities);
            if (QueryEvidence.ContainsSecret(JsonValue.Create(cleaned.Text), secrets))
            {
                return Result.Fail<SampleRecording>(new Problem(
                    "profile.sample_secret", $"The answer to {exchange.Method} {exchange.Path} holds a secret's value, so no recording was kept."));
            }

            var file = string.Create(CultureInfo.InvariantCulture, $"recordings/{index:000}-{exchange.Method.ToLowerInvariant()}-{Slug(exchange.Path)}{(cleaned.Json is null && exchange.Body.TrimStart().StartsWith('<') ? ".xml" : ".json")}");
            responses.Add(new SampledResponse(exchange.Method, exchange.Path, exchange.Query, exchange.Status, file, cleaned.Text) { Json = cleaned.Json });
        }

        return Result.Ok(new SampleRecording(responses));
    }

    /// <summary>
    /// Writes <paramref name="name"/>.yaml and its recordings into the profile's tests folder. Secrets get placeholder values; the expected
    /// result of each check is the one the person confirmed.
    /// </summary>
    public static void WriteTestCase(
        SampleRecording sample,
        string testsFolder,
        string name,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyList<string> secretNames,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> expect)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretNames);
        ArgumentNullException.ThrowIfNull(expect);
        Directory.CreateDirectory(Path.Combine(testsFolder, "recordings"));
        foreach (var response in sample.Responses)
        {
            File.WriteAllText(Path.Combine(testsFolder, response.File), response.Body);
        }

        var text = new StringBuilder()
            .Append("# A profile test recorded by hound onboard's sample run: identities hashed, tokens replaced, the API's address as {{api}}.\n")
            .Append(CultureInfo.InvariantCulture, $"description: {Quote($"Sample run of {now:yyyy-MM-dd}, with the results the reviewer confirmed.")}\n")
            .Append(CultureInfo.InvariantCulture, $"now: {now.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\n");
        Map(text, "settings", settings.Where(s => s.Key is not (RestSettings.Profile or RestSettings.ProfilesPath)).Select(s => (s.Key, s.Value)));
        Map(text, "secrets", secretNames.Select(s => (s, $"recorded-{s}")));
        text.Append("responses:\n");
        foreach (var r in sample.Responses)
        {
            var query = r.Query.Count == 0 ? "" : $", query: {{ {string.Join(", ", r.Query.Select(q => $"{Quote(q.Key)}: {Quote(q.Value)}"))} }}";
            var status = r.Status == 200 ? "" : string.Create(CultureInfo.InvariantCulture, $", status: {r.Status}");
            text.Append(CultureInfo.InvariantCulture, $"  - {{ {r.Method.ToLowerInvariant()}: {Quote(r.Path)}{query}{status}, file: {Quote(r.File)} }}\n");
        }

        Map(text, "expect", expect.Select(e => (e.Key, e.Value)));
        File.WriteAllText(Path.Combine(testsFolder, name + ".yaml"), text.ToString());
    }

    private static void Map(StringBuilder text, string key, IEnumerable<(string Key, string Value)> pairs)
    {
        var all = pairs.ToArray();
        text.Append(CultureInfo.InvariantCulture, $"{key}:{(all.Length == 0 ? " {}" : "")}\n");
        foreach (var (k, v) in all)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {Quote(k)}: {Quote(v)}\n");
        }
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value, Readable);

    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static (string Text, JsonNode? Json) Clean(string body, IReadOnlyCollection<string> authorities, HashSet<string> identities)
    {
        JsonNode? json;
        try
        {
            json = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            json = null;
        }

        if (json is not null)
        {
            Hash(json, identities);
            body = json.ToJsonString(Indented);
        }

        foreach (var authority in authorities)
        {
            body = body.Replace(authority, "{{api}}", StringComparison.OrdinalIgnoreCase);
        }

        return (body, json is null ? null : JsonNode.Parse(body));
    }

    private static void Hash(JsonNode node, HashSet<string> identities)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    {
                        if (TokenFields.Contains(key, StringComparer.OrdinalIgnoreCase))
                        {
                            obj[key] = RecordedToken;
                        }
                        else if (identities.Contains(key))
                        {
                            obj[key] = QueryEvidence.Hash(value.GetValue<string>());
                        }
                    }
                    else if (obj[key] is { } child)
                    {
                        Hash(child, identities);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array.Where(i => i is not null))
                {
                    Hash(item!, identities);
                }

                break;
        }
    }

    private static string Slug(string path)
    {
        var slug = new string([.. path.Trim('/').Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);
        return slug.Length == 0 ? "root" : slug.Length > 60 ? slug[..60] : slug;
    }

    /// <summary>Keeps every request and its answer, passing them through unchanged.</summary>
    private sealed class Recorder(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<Exchange> Exchanges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var url = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            Exchanges.Add(new Exchange(
                request.Method.Method,
                url.AbsolutePath,
                query.AllKeys.OfType<string>().ToDictionary(k => k, k => query[k] ?? "", StringComparer.Ordinal),
                (int)response.StatusCode,
                body,
                [url.GetLeftPart(UriPartial.Authority)]));
            var copy = new HttpResponseMessage(response.StatusCode) { Content = new StringContent(body, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json"), RequestMessage = request };
            foreach (var header in response.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Dispose();
            return copy;
        }
    }

    private sealed record Exchange(string Method, string Path, IReadOnlyDictionary<string, string> Query, int Status, string Body, IReadOnlyCollection<string> Authorities);

    /// <summary>Waits for real, so a sample run keeps to the API's rate limit.</summary>
    private sealed class RealWait : IDelay
    {
        public static RealWait Instance { get; } = new();

        public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
    }
}
