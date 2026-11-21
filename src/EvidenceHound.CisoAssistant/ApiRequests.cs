using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EvidenceHound.CisoAssistant;

/// <summary>Sending a request to CISO Assistant and reading its JSON answer, with failures as problems rather than exceptions.</summary>
internal static class ApiRequests
{
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

    /// <summary>The objects in a list response: a paginated <c>{"results": [...]}</c> or a bare array.</summary>
    public static List<JsonNode> Results(JsonNode page) =>
        [.. (page as JsonArray ?? page["results"] as JsonArray ?? []).OfType<JsonNode>()];
}
