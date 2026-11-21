using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EvidenceHound.CisoAssistant;

/// <summary>REST adapter for <see cref="ICisoAssistantClient"/>. Paths are relative to the <c>/api/</c> base address.</summary>
internal sealed class CisoAssistantClient(HttpClient http) : ICisoAssistantClient
{
    public async Task<Result<Unit>> CheckHealthAsync(CancellationToken cancellationToken) =>
        (await SendAsync(new HttpRequestMessage(HttpMethod.Get, "health/"), "for the health check", cancellationToken)).Map(_ => Unit.Value);

    public Task<Result<string>> FindRequirementAssessmentAsync(string complianceAssessmentRefId, string requirementRefId, CancellationToken cancellationToken) =>
        FindOneAsync("requirement-assessments/", "requirement assessment", Selector.RequirementInAudit(complianceAssessmentRefId, requirementRefId), cancellationToken);

    public async Task<Result<Unit>> UpdateRequirementAssessmentAsync(string requirementAssessmentId, RequirementAssessmentValues values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var body = new JsonObject
        {
            ["result"] = Names.Of(values.Result),
            ["observation"] = values.Observation,
            ["evidences"] = new JsonArray([.. values.EvidenceIds.Select(id => JsonValue.Create(id))]),
        };
        return (await PatchAsync($"requirement-assessments/{Uri.EscapeDataString(requirementAssessmentId)}/", body, "to update a requirement assessment", cancellationToken))
            .Map(_ => Unit.Value);
    }

    public async Task<Result<RemoteEvidence>> FindOrCreateEvidenceAsync(string refId, string name, CancellationToken cancellationToken)
    {
        var search = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"evidences/?search={Uri.EscapeDataString(refId)}"), $"to find evidence {refId}", cancellationToken);
        var matches = search.Map(page => Results(page).Where(o => (string?)o["ref_id"] == refId).ToList());
        return await matches.Match(
            found => found.Count switch
            {
                0 => CreateEvidenceAsync(refId, name, cancellationToken),
                1 => LatestHashAsync(Id(found[0]), cancellationToken),
                _ => Task.FromResult(Result.Fail<RemoteEvidence>(ApiProblems.Ambiguous("evidence", Selector.ByRefId(refId), found.Count))),
            },
            problems => Task.FromResult(Result.Fail<RemoteEvidence>(problems)));
    }

    public async Task<Result<Unit>> UploadEvidenceRevisionAsync(string evidenceId, EvidenceFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var form = new MultipartFormDataContent
        {
            { new StringContent(evidenceId), "evidence" },
            { new ReadOnlyMemoryContent(file.Content), "attachment", file.FileName },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "evidence-revisions/") { Content = form };
        return (await SendAsync(request, $"to upload {file.FileName}", cancellationToken)).Map(_ => Unit.Value);
    }

    public Task<Result<Unit>> LinkEvidenceToAppliedControlAsync(string controlRefId, string evidenceId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public async Task<Result<Unit>> UpdateAppliedControlAsync(string refId, ControlState status, CancellationToken cancellationToken)
    {
        var found = await FindOneAsync("applied-controls/", "applied control", Selector.ByRefId(refId), cancellationToken);
        return await found.Match(
            async id => (await PatchAsync($"applied-controls/{Uri.EscapeDataString(id)}/", new JsonObject { ["status"] = Names.Of(status) }, $"to update applied control {refId}", cancellationToken))
                .Map(_ => Unit.Value),
            problems => Task.FromResult(Result.Fail<Unit>(problems)));
    }

    private async Task<Result<RemoteEvidence>> CreateEvidenceAsync(string refId, string name, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "evidences/") { Content = Json(new JsonObject { ["name"] = name, ["ref_id"] = refId }) };
        return (await SendAsync(request, $"to create evidence {refId}", cancellationToken)).Map(created => new RemoteEvidence(Id(created), null));
    }

    private async Task<Result<RemoteEvidence>> LatestHashAsync(string evidenceId, CancellationToken cancellationToken)
    {
        var path = $"evidence-revisions/?evidence={Uri.EscapeDataString(evidenceId)}&ordering=-version&limit=1";
        var page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), "to read the latest evidence revision", cancellationToken);
        return page.Map(p => new RemoteEvidence(evidenceId, Results(p).Select(r => (string?)r["attachment_hash"]).FirstOrDefault()));
    }

    private async Task<Result<string>> FindOneAsync(string path, string what, Selector selector, CancellationToken cancellationToken)
    {
        var page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{path}?{selector.ToQueryString()}"), $"to find the {what} for {selector}", cancellationToken);
        return page.Bind(p => Results(p) switch
        {
            [] => Result.Fail<string>(ApiProblems.NotFound(what, selector)),
            [var only] => Result.Ok(Id(only)),
            var many => Result.Fail<string>(ApiProblems.Ambiguous(what, selector, many.Count)),
        });
    }

    private Task<Result<JsonNode>> PatchAsync(string path, JsonObject body, string action, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Patch, path) { Content = Json(body) }, action, cancellationToken);

    private async Task<Result<JsonNode>> SendAsync(HttpRequestMessage request, string action, CancellationToken cancellationToken)
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

    /// <summary>A JSON body with a known length: CISO Assistant's server reads no chunked request body.</summary>
    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>The objects in a list response: a paginated <c>{"results": [...]}</c> or a bare array.</summary>
    private static List<JsonNode> Results(JsonNode page) =>
        [.. (page as JsonArray ?? page["results"] as JsonArray ?? []).OfType<JsonNode>()];

    private static string Id(JsonNode node) => (string?)node["id"] ?? throw new InvalidOperationException("CISO Assistant returned an object without an id.");
}
