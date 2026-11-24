using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EvidenceHound.CisoAssistant;

/// <summary>Sending a request to CISO Assistant and reading its JSON answer, with failures as problems rather than exceptions.</summary>
internal static class ApiRequests
{
    private static readonly int PageSize = 500;

    public static async Task<Result<JsonNode>> SendForJsonAsync(this HttpClient http, HttpRequestMessage request, string action, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return response.IsSuccessStatusCode
                    ? Result.Ok(string.IsNullOrWhiteSpace(body) ? new JsonObject() : JsonNode.Parse(body) ?? new JsonObject())
                    : Result.Fail<JsonNode>(ApiProblems.FromStatus((int)response.StatusCode, body, action));
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
            {
                return Result.Fail<JsonNode>(ApiProblems.Unavailable($"CISO Assistant could not be reached {action}: {ex.Message}"));
            }
        }
    }

    /// <summary>A file CISO Assistant generates, such as an audit's Word report, as bytes.</summary>
    public static async Task<Result<ReadOnlyMemory<byte>>> SendForBytesAsync(this HttpClient http, HttpRequestMessage request, string action, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail<ReadOnlyMemory<byte>>(ApiProblems.FromStatus((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), action));
                }

                return Result.Ok<ReadOnlyMemory<byte>>(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
            {
                return Result.Fail<ReadOnlyMemory<byte>>(ApiProblems.Unavailable($"CISO Assistant could not be reached {action}: {ex.Message}"));
            }
        }
    }

    /// <summary>Every object of a list, paged by limit and offset. <paramref name="path"/> may carry a query of its own.</summary>
    public static async Task<Result<List<JsonNode>>> ListAllAsync(this HttpClient http, string path, CancellationToken cancellationToken)
    {
        var listed = new List<JsonNode>();
        while (true)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var page = await http.SendForJsonAsync(
                new HttpRequestMessage(HttpMethod.Get, string.Create(CultureInfo.InvariantCulture, $"{path}{separator}limit={PageSize}&offset={listed.Count}")),
                $"to list {path.Split('?')[0]}",
                cancellationToken);
            if (page is not Result<JsonNode>.Ok { Value: var found })
            {
                return page.Map(_ => listed);
            }

            var results = Results(found);
            listed.AddRange(results);
            if (results.Count == 0 || listed.Count >= ((int?)found["count"] ?? 0))
            {
                return Result.Ok(listed);
            }
        }
    }

    /// <summary>The objects in a list response: a paginated <c>{"results": [...]}</c> or a bare array.</summary>
    public static List<JsonNode> Results(JsonNode page) =>
        [.. (page as JsonArray ?? page["results"] as JsonArray ?? []).OfType<JsonNode>()];
}
