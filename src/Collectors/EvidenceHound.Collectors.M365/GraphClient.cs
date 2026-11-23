using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Reads Graph collections: a bearer token on every request, <c>@odata.nextLink</c> followed until the last page (only to the
/// Graph host), and <c>Retry-After</c> honoured on 429 and 503.
/// </summary>
internal sealed class GraphClient(HttpClient http, TokenCredential credential, Uri graphBase, IGraphWait wait)
{
    public const int MaxAttempts = 5;

    /// <summary>The problem code for a feature the tenant is not licensed for, such as Privileged Identity Management.</summary>
    public const string NotLicensed = "m365.graph_not_licensed";

    /// <summary>The problem code for a permission the app registration lacks.</summary>
    public const string Forbidden = "m365.graph_forbidden";

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(5);

    private readonly TokenRequestContext _scope = new([new Uri(graphBase, ".default").ToString()]);

    public async Task<Result<GraphCollection>> GetAllAsync(GraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new JsonArray();
        var pages = 0;
        Uri? next = new(graphBase, request.Path);
        while (next is not null)
        {
            var page = await GetPageAsync(request, next, cancellationToken);
            if (page is Result<JsonObject>.Fail failed)
            {
                return Result.Fail<GraphCollection>(failed.Errors);
            }

            var body = ((Result<JsonObject>.Ok)page).Value;
            pages++;
            foreach (var item in body[request.ItemsProperty] as JsonArray ?? [])
            {
                items.Add(item?.DeepClone());
            }

            var link = NextLink(body);
            if (link is Result<NextPage>.Fail foreign)
            {
                return Result.Fail<GraphCollection>(foreign.Errors);
            }

            next = ((Result<NextPage>.Ok)link).Value.Url;
        }

        return Result.Ok(new GraphCollection(request, items, pages));
    }

    private async Task<Result<JsonObject>> GetPageAsync(GraphRequest request, Uri url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await TokenAsync(cancellationToken);
            if (token is Result<string>.Fail refused)
            {
                return Result.Fail<JsonObject>(refused.Errors);
            }

            using var message = new HttpRequestMessage(request.Body is null ? HttpMethod.Get : HttpMethod.Post, url);
            if (request.Body is { } payload)
            {
                message.Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            }

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ((Result<string>.Ok)token).Value);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (IsThrottled(response.StatusCode))
            {
                if (attempt == MaxAttempts)
                {
                    return Result.Fail<JsonObject>(new Problem(
                        "m365.graph_throttled", $"Microsoft Graph kept throttling {request.Path} ({(int)response.StatusCode}) after {MaxAttempts} attempts. Try again later."));
                }

                await wait.WaitAsync(RetryAfter(response), cancellationToken);
                continue;
            }

            return response.IsSuccessStatusCode ? Parse(request, content) : Refusal(request, response.StatusCode, content);
        }
    }

    private async Task<Result<string>> TokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Result.Ok((await credential.GetTokenAsync(_scope, cancellationToken)).Token);
        }
        catch (AuthenticationFailedException exception)
        {
            return Result.Fail<string>(new Problem("m365.auth_failed", $"Could not get a Microsoft Graph token: {exception.Message}"));
        }
    }

    private Result<NextPage> NextLink(JsonObject page)
    {
        if (page["@odata.nextLink"]?.GetValue<string>() is not { } link)
        {
            return Result.Ok(new NextPage(null));
        }

        // The bearer token goes with every request, so pages are only read from the Graph host the collector was configured with.
        return Uri.TryCreate(link, UriKind.Absolute, out var uri) && Uri.Compare(uri, graphBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
            ? Result.Ok(new NextPage(uri))
            : Result.Fail<NextPage>(new Problem("m365.graph_next_link_foreign", $"Microsoft Graph returned a next page on another host ({(uri?.Host ?? link)}); it was not followed."));
    }

    private static bool IsThrottled(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta
            ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultWait);
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > LongestWait ? LongestWait : delay;
    }

    private static Result<JsonObject> Parse(GraphRequest request, string content)
    {
        try
        {
            return JsonNode.Parse(content) is JsonObject page
                ? Result.Ok(page)
                : Result.Fail<JsonObject>(InvalidResponse(request));
        }
        catch (JsonException)
        {
            return Result.Fail<JsonObject>(InvalidResponse(request));
        }
    }

    private static Problem InvalidResponse(GraphRequest request) =>
        new("m365.graph_invalid_response", $"Microsoft Graph answered {request.Path} with something that is not a JSON object.");

    private static Result<JsonObject> Refusal(GraphRequest request, HttpStatusCode status, string content)
    {
        var (code, text) = ErrorOf(content);
        var detail = code is null ? $"{(int)status}" : $"{(int)status} {code}";
        var problem = status switch
        {
            _ when MentionsLicence(code, text) => new Problem(NotLicensed, $"The tenant is not licensed for {request.Path} ({detail})."),
            HttpStatusCode.Unauthorized => new Problem("m365.graph_unauthorized", $"Microsoft Graph refused the token for {request.Path} ({detail}). Check the tenant and client IDs and the credential."),
            HttpStatusCode.Forbidden => new Problem(
                Forbidden, $"Microsoft Graph refused {request.Path} ({detail}). The app registration needs the application permission {request.Permission} with admin consent."),
            _ => new Problem("m365.graph_failed", $"Microsoft Graph answered {request.Path} with {detail}."),
        };
        return Result.Fail<JsonObject>(problem);
    }

    private static bool MentionsLicence(string? code, string? text) =>
        (code ?? string.Empty).Contains("license", StringComparison.OrdinalIgnoreCase) || (text ?? string.Empty).Contains("license", StringComparison.OrdinalIgnoreCase);

    private static (string? Code, string? Message) ErrorOf(string content)
    {
        try
        {
            var error = JsonNode.Parse(content)?["error"];
            return (error?["code"]?.GetValue<string>(), error?["message"]?.GetValue<string>());
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>The next page's address, or none after the last page.</summary>
    private sealed record NextPage(Uri? Url);
}
