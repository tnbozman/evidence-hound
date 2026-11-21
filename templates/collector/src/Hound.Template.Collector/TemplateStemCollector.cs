using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Hound.Template.Collector;

/// <summary>
/// Collects from TemplateStem. The sample reads <c>api/status</c> and reports <c>template_type.healthy</c>; replace it with
/// the calls and checks your system needs. Expected failures are returned as problems, never thrown.
/// </summary>
public sealed class TemplateStemCollector : ICollector
{
    public const string HealthyCheck = "template_type.healthy";

    private static readonly string EvidenceFile = "status.json";

    private static readonly Action<ILogger, Uri, Exception?> Unreachable =
        LoggerMessage.Define<Uri>(LogLevel.Warning, new EventId(1, "Unreachable"), "TemplateStem at {BaseUrl} did not answer");

    public string Type => "template_type";

    public string Version => "0.1.0";

    public CollectorDescription Describe() => new(
        [HealthyCheck],
        [new("base_url", "TemplateStem address ending in /, for example https://template_type.example.com/.", Required: true)],
        [new("api_token", "TemplateStem API token.", Required: true)]);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(context.Settings.Find("base_url"), UriKind.Absolute, out var baseUrl))
        {
            return Failed("template_type.base_url_invalid", "Setting base_url must be an absolute URL, for example https://template_type.example.com/.");
        }

        if (!context.Secrets.TryGetValue("api_token", out var token))
        {
            return Failed("template_type.token_missing", "Secret api_token is not set.");
        }

        var status = await FetchStatusAsync(context, baseUrl, token, cancellationToken);
        return status.Bind(body => Evaluate(body, context));
    }

    /// <summary>Pure: turns the status response into a check result that cites it as evidence.</summary>
    private static Result<CollectionResult> Evaluate(byte[] status, CollectorContext context)
    {
        bool healthy;
        try
        {
            using var document = JsonDocument.Parse(status);
            healthy = document.RootElement.TryGetProperty("healthy", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return Failed("template_type.status_invalid", "TemplateStem's status response is not JSON.");
        }

        var (outcome, observation) = healthy
            ? (CheckOutcome.Pass, "TemplateStem reports itself healthy.")
            : (CheckOutcome.Fail, "TemplateStem does not report itself healthy.");

        return CheckId.Create(HealthyCheck)
            .Bind(id => CheckResult.Create(id, outcome, observation, [EvidenceFile], context.Clock.UtcNow, context.Name))
            .Map(check => new CollectionResult([check], [CollectedEvidence.FromBytes(EvidenceFile, "application/json", status)], []));
    }

    private async Task<Result<byte[]>> FetchStatusAsync(CollectorContext context, Uri baseUrl, Secret token, CancellationToken cancellationToken)
    {
        using var client = context.HttpClients.CreateClient(Type);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "api/status"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Result.Fail<byte[]>(new Problem("template_type.auth_refused", "TemplateStem refused the API token.")),
                _ when !response.IsSuccessStatusCode => Result.Fail<byte[]>(new Problem("template_type.unavailable", $"TemplateStem answered {(int)response.StatusCode}.")),
                _ => Result.Ok(await response.Content.ReadAsByteArrayAsync(cancellationToken)),
            };
        }
        catch (HttpRequestException e)
        {
            Unreachable(context.Logger, baseUrl, e);
            return Result.Fail<byte[]>(new Problem("template_type.unreachable", $"TemplateStem at {baseUrl} did not answer."));
        }
    }

    private static Result<CollectionResult> Failed(string code, string message) => Result.Fail<CollectionResult>(new Problem(code, message));
}
