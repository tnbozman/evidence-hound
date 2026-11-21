using System.Collections.Immutable;
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
    private static readonly int PageSize = 500;

    public async Task<Result<Unit>> CheckHealthAsync(CancellationToken cancellationToken) =>
        (await SendAsync(new HttpRequestMessage(HttpMethod.Get, "health/"), "for the health check", cancellationToken)).Map(_ => Unit.Value);

    public async Task<Result<IReadOnlyList<RemoteRequirementAssessment>>> FindRequirementAssessmentsAsync(
        string complianceAssessmentRefId, RequirementKey requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var selector = requirement.Kind == RequirementKeyKind.Urn
            ? Selector.RequirementUrnInAudit(complianceAssessmentRefId, requirement.Value)
            : Selector.RequirementInAudit(complianceAssessmentRefId, requirement.Value);
        var page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"requirement-assessments/?{selector.ToQueryString()}"), $"to find the requirement assessments for {selector}", cancellationToken);
        return page.Bind(p => Results(p) switch
        {
            [] => Result.Fail<IReadOnlyList<RemoteRequirementAssessment>>(ApiProblems.NotFound("requirement assessment", selector)),
            var rows => Result.Ok<IReadOnlyList<RemoteRequirementAssessment>>([.. rows.Select(Row)]),
        });
    }

    public async Task<Result<RemoteAudit>> FindAuditAsync(string complianceAssessmentRefId, CancellationToken cancellationToken)
    {
        // The read serializer lists the selected groups by name; the write data has their ref_ids.
        var found = await FindOneAsync("compliance-assessments/", "audit", Selector.ByRefId(complianceAssessmentRefId), cancellationToken);
        return await found.BindAsync(async id =>
            (await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"compliance-assessments/{Uri.EscapeDataString(id)}/object/"), $"to read audit {complianceAssessmentRefId}", cancellationToken))
                .Map(audit => new RemoteAudit(id, Strings(audit["selected_implementation_groups"]))));
    }

    // Pages by limit and offset rather than following "next", whose host is whatever the server believes it is.
    public Task<Result<IReadOnlyList<CurrentRow>>> ListRequirementAssessmentsAsync(string complianceAssessmentRefId, CancellationToken cancellationToken) =>
        ListRowsAsync(complianceAssessmentRefId, ImmutableList<CurrentRow>.Empty, cancellationToken);

    public async Task<Result<IReadOnlyList<RemoteEvidence>>> FindEvidenceAsync(string refId, CancellationToken cancellationToken)
    {
        var matches = await SearchEvidenceAsync(refId, cancellationToken);
        return await matches.Match(
            found => found.Count switch
            {
                0 => Task.FromResult(Result.Ok<IReadOnlyList<RemoteEvidence>>([])),
                1 => AsList(LatestHashAsync(Id(found[0]), cancellationToken)),
                _ => Task.FromResult(Result.Fail<IReadOnlyList<RemoteEvidence>>(ApiProblems.Ambiguous("evidence", Selector.ByRefId(refId), found.Count))),
            },
            problems => Task.FromResult(Result.Fail<IReadOnlyList<RemoteEvidence>>(problems)));
    }

    public async Task<Result<ControlState>> FindAppliedControlStatusAsync(string refId, CancellationToken cancellationToken)
    {
        var selector = Selector.ByRefId(refId);
        var page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"applied-controls/?{selector.ToQueryString()}"), $"to find the applied control for {selector}", cancellationToken);
        return page.Bind(p => Results(p) switch
        {
            [] => Result.Fail<ControlState>(ApiProblems.NotFound("applied control", selector)),
            [var only] => ControlStateOf((string?)only["status"], refId),
            var many => Result.Fail<ControlState>(ApiProblems.Ambiguous("applied control", selector, many.Count)),
        });
    }

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
        var matches = await SearchEvidenceAsync(refId, cancellationToken);
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
        FindOneAsync("applied-controls/", "applied control", Selector.ByRefId(controlRefId), cancellationToken)
            .BindAsync(id => LinkAsync(id, controlRefId, evidenceId, cancellationToken));

    public async Task<Result<Unit>> UpdateAppliedControlAsync(string refId, ControlState status, CancellationToken cancellationToken)
    {
        var found = await FindOneAsync("applied-controls/", "applied control", Selector.ByRefId(refId), cancellationToken);
        return await found.Match(
            async id => (await PatchAsync($"applied-controls/{Uri.EscapeDataString(id)}/", new JsonObject { ["status"] = Names.Of(status) }, $"to update applied control {refId}", cancellationToken))
                .Map(_ => Unit.Value),
            problems => Task.FromResult(Result.Fail<Unit>(problems)));
    }

    private async Task<Result<Unit>> LinkAsync(string controlId, string controlRefId, string evidenceId, CancellationToken cancellationToken)
    {
        var path = $"applied-controls/{Uri.EscapeDataString(controlId)}/";
        var control = await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), $"to read applied control {controlRefId}", cancellationToken);
        return await control.BindAsync(async found =>
        {
            // The read serializer lists evidences as {"id", "str"} objects; plain IDs are accepted too.
            List<string> linked = [.. (found["evidences"] as JsonArray ?? []).Select(e => e is JsonObject o ? (string?)o["id"] : (string?)e).OfType<string>()];
            if (linked.Contains(evidenceId))
            {
                return Result.Ok(Unit.Value);
            }

            var body = new JsonObject { ["evidences"] = new JsonArray([.. linked.Append(evidenceId).Select(e => JsonValue.Create(e))]) };
            return (await PatchAsync(path, body, $"to link evidence to applied control {controlRefId}", cancellationToken)).Map(_ => Unit.Value);
        });
    }

    private async Task<Result<IReadOnlyList<CurrentRow>>> ListRowsAsync(string auditRefId, ImmutableList<CurrentRow> listed, CancellationToken cancellationToken)
    {
        var path = $"requirement-assessments/?{Selector.RowsOfAudit(auditRefId).ToQueryString()}&limit={PageSize}&offset={listed.Count}";
        var page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), $"to list the rows of audit {auditRefId}", cancellationToken);
        return await page
            .Bind(p => Result.Combine(Results(p).Select(StateRow)).Map(found => (Rows: listed.AddRange(found), More: found.Count > 0 && listed.Count + found.Count < ((int?)p["count"] ?? 0))))
            .Match(
                next => next.More ? ListRowsAsync(auditRefId, next.Rows, cancellationToken) : Task.FromResult(Result.Ok<IReadOnlyList<CurrentRow>>(next.Rows)),
                problems => Task.FromResult(Result.Fail<IReadOnlyList<CurrentRow>>(problems)));
    }

    private static async Task<Result<IReadOnlyList<RemoteEvidence>>> AsList(Task<Result<RemoteEvidence>> found) =>
        (await found).Map(e => (IReadOnlyList<RemoteEvidence>)[e]);

    private async Task<Result<List<JsonNode>>> SearchEvidenceAsync(string refId, CancellationToken cancellationToken)
    {
        var search = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"evidences/?search={Uri.EscapeDataString(refId)}"), $"to find evidence {refId}", cancellationToken);
        return search.Map(page => Results(page).Where(o => (string?)o["ref_id"] == refId).ToList());
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

    /// <summary>A requirement assessment from the list endpoint, which nests its requirement node.</summary>
    private static RemoteRequirementAssessment Row(JsonNode row)
    {
        var requirement = row["requirement"] as JsonObject ?? throw new InvalidOperationException("CISO Assistant returned a requirement assessment without its requirement.");
        return new RemoteRequirementAssessment(Id(row), (string?)requirement["urn"] ?? "", (string?)requirement["ref_id"], Strings(requirement["implementation_groups"]));
    }

    /// <summary>A requirement assessment with its state: result, observation and evidence IDs (the read serializer nests each as {"id", "str"}).</summary>
    private static Result<CurrentRow> StateRow(JsonNode row)
    {
        var at = Row(row);
        var result = (string?)row["result"] ?? "not_assessed";
        return Enum.GetValues<ComplianceResult>().Where(r => Names.Of(r) == Normalised(result)).ToArray() is [var known]
            ? Result.Ok(new CurrentRow(
                at.Id, at.Urn, at.RefId, at.Groups, known, (string?)row["observation"] ?? "",
                [.. (row["evidences"] as JsonArray ?? []).Select(e => e is JsonObject o ? (string?)o["id"] : (string?)e).OfType<string>()]))
            : Result.Fail<CurrentRow>(Unexpected($"requirement assessment {at.Id} has result '{result}'"));
    }

    private static Result<ControlState> ControlStateOf(string? status, string refId) =>
        Enum.GetValues<ControlState>().Where(s => Names.Of(s) == Normalised(status ?? "")).ToArray() is [var known]
            ? Result.Ok(known)
            : Result.Fail<ControlState>(Unexpected($"applied control {refId} has status '{status}'"));

    /// <summary>A choice's value from its value or its display name: "To do" is "to_do".</summary>
    private static string Normalised(string value) => value.Trim().ToLowerInvariant().Replace(' ', '_');

    private static Problem Unexpected(string what) => new("ciso_assistant.rejected", $"CISO Assistant returned something this version does not know: {what}.");

    private static List<string> Strings(JsonNode? node) => [.. (node as JsonArray ?? []).Select(n => (string?)n).OfType<string>()];

    private static string Id(JsonNode node) => (string?)node["id"] ?? throw new InvalidOperationException("CISO Assistant returned an object without an id.");
}
