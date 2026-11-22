using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>An answer from Sophos: its status and its body, parsed as a JSON object when it is one.</summary>
internal sealed record SophosResponse(HttpStatusCode Status, JsonObject? Body)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>The <c>errorCode</c> or <c>error</c> Sophos gave, or the status code when it gave neither.</summary>
    public string Error => Body?["errorCode"]?.GetValue<string>() ?? Body?["error"]?.GetValue<string>() ?? $"{(int)Status}";
}

/// <summary>Sends one request to Sophos. A host that cannot be reached is a problem, not an exception.</summary>
internal static class SophosHttp
{
    public static async Task<Result<SophosResponse>> SendAsync(HttpClient http, HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result.Ok(new SophosResponse(response.StatusCode, Parse(content)));
        }
        catch (HttpRequestException exception)
        {
            return Result.Fail<SophosResponse>(new Problem("sophos.unreachable", $"Could not reach Sophos for {what} at {request.RequestUri?.Host}: {exception.Message}"));
        }
    }

    /// <summary>A GET for JSON with the bearer token.</summary>
    public static HttpRequestMessage Get(Uri url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>A successful answer's JSON object, or a problem saying what Sophos answered instead.</summary>
    public static Result<JsonObject> Body(SophosResponse response, string what) => response switch
    {
        { IsSuccess: true, Body: { } body } => Result.Ok(body),
        { IsSuccess: true } => Result.Fail<JsonObject>(new Problem("sophos.invalid_response", $"Sophos answered {what} with something that is not a JSON object.")),
        { Status: HttpStatusCode.Forbidden } => Result.Fail<JsonObject>(new Problem(
            "sophos.forbidden", $"Sophos Central refused {what} ({response.Error}). The API credential needs the Service Principal Read-Only role.")),
        { Status: HttpStatusCode.Unauthorized } => Result.Fail<JsonObject>(new Problem("sophos.unauthorized", $"Sophos Central refused the token for {what} ({response.Error}).")),
        _ => Result.Fail<JsonObject>(new Problem("sophos.request_failed", $"Sophos answered {what} with {(int)response.Status} ({response.Error}).")),
    };

    private static JsonObject? Parse(string content)
    {
        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
