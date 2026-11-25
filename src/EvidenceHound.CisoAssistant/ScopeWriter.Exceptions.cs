using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Security exceptions and risk acceptance requests (s15-03). Neither is ever written with a status or state: an approver decides those in
/// CISO Assistant, so the repo only asks.
/// </summary>
internal sealed partial class ScopeWriter
{
    /// <summary>A security exception by <c>ref_id</c>, with its owners, expiry and the systems, controls and audit rows it covers.</summary>
    private async Task<Result<Unit>> SecurityExceptionAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("security-exceptions/", cancellationToken);
        return await listed.BindAsync(async exceptions =>
        {
            var existing = IdByRefId(exceptions, ScopeKind.SecurityException).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("security-exceptions", existing, write, cancellationToken);
            }

            var folder = folders.FolderOf(declared);
            var owners = await ActorsAsync(Split(Field(declared, "owners")), cancellationToken);
            var assets = await IdsAsync("assets/", "name", Split(Field(declared, "systems")), "system", cancellationToken);
            var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
            var rows = await RowIdsAsync(Split(Field(declared, "requirements")), cancellationToken);
            return await Result.Combine([folder.Map(Node), owners.Map(o => (JsonNode)o), assets.Map(a => (JsonNode)a), controls.Map(c => (JsonNode)c), rows.Map(r => (JsonNode)r)])
                .BindAsync(async resolved =>
                {
                    var body = new JsonObject
                    {
                        ["ref_id"] = declared.Key,
                        ["name"] = Field(declared, "name"),
                        ["description"] = Described(declared, write.Change),
                        ["folder"] = resolved[0].DeepClone(),
                        ["severity"] = ScopeExceptionRequest.Severities.ToList().IndexOf(Field(declared, "severity") ?? "undefined") - 1,
                        ["owners"] = resolved[1].DeepClone(),
                        ["expiration_date"] = Field(declared, "expiration_date"),
                        ["observation"] = Field(declared, "observation"),
                        ["assets"] = resolved[2].DeepClone(),
                        ["applied_controls"] = resolved[3].DeepClone(),
                        ["requirement_assessments"] = resolved[4].DeepClone(),
                    };
                    Drop(body, write.Change, "expiration_date", "observation");
                    return write.Change.Action == ScopeAction.Create
                        ? await SendAsync(HttpMethod.Post, "security-exceptions/", body, $"to create security exception {declared.Key}", cancellationToken)
                        : await UpdateAsync("security-exceptions", existing, body, write, cancellationToken);
                });
        });
    }

    /// <summary>A request to accept a scenario's risk, under <see cref="RiskObjects.AcceptanceRefId"/>; its approver accepts or rejects it in CISO Assistant.</summary>
    private async Task<Result<Unit>> RiskAcceptanceAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var refId = RiskObjects.AcceptanceRefId(write.Change.Key);
        var listed = await http.ListAllAsync("risk-acceptances/", cancellationToken);
        return await listed.BindAsync(async acceptances =>
        {
            var existing = acceptances.FirstOrDefault(a => Text(a["ref_id"]) == refId) is { } held ? Id(held) : null;
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("risk-acceptances", existing, write, cancellationToken);
            }

            var scenario = await ScenarioIdAsync(Field(declared, "scenario") ?? write.Change.Key, cancellationToken);
            return await Result.Combine([folders.FolderOf(declared).Map(Node), scenario.Map(Node)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["ref_id"] = refId,
                    ["name"] = Field(declared, "name"),
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["risk_scenarios"] = new JsonArray(resolved[1].DeepClone()),
                    ["justification"] = Field(declared, "justification"),
                };
                Drop(body, write.Change, "justification");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "risk-acceptances/", body, $"to create risk acceptance {refId}", cancellationToken)
                    : await UpdateAsync("risk-acceptances", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>A scenario's ID by its key, <c>study/scenario</c>.</summary>
    private async Task<Result<string>> ScenarioIdAsync(string key, CancellationToken cancellationToken)
    {
        var (studyRef, refId) = key.Split('/', 2) is [var s, var r] ? (s, r) : (key, key);
        var studies = await http.ListAllAsync("risk-assessments/", cancellationToken);
        return await studies.BindAsync(async found =>
        {
            if (IdByRefId(found, ScopeKind.RiskStudy).GetValueOrDefault(studyRef) is not { } study)
            {
                return Result.Fail<string>(ApiProblems.NotFound("risk study", $"'{studyRef}'"));
            }

            return (await http.ListAllAsync($"risk-scenarios/?risk_assessment={Uri.EscapeDataString(study)}", cancellationToken)).Bind(scenarios =>
                scenarios.FirstOrDefault(s => Text(s["ref_id"]) == refId) is { } held ? Result.Ok(Id(held)) : Result.Fail<string>(ApiProblems.NotFound("risk scenario", $"'{key}'")));
        });
    }
}
