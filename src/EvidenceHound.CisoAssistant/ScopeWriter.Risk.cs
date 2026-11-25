using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Risk studies (risk assessments) and their scenarios (s15-02). CISO Assistant computes the risk levels from the ratings.</summary>
internal sealed partial class ScopeWriter
{
    private static readonly string[] RatingNumbers = ["current_proba", "current_impact", "residual_proba", "residual_impact"];
    private static readonly string[] InherentNumbers = ["inherent_proba", "inherent_impact"];

    /// <summary>A risk study by <c>ref_id</c>, in its domain and optional perimeter, on a risk matrix found by URN, with its dates and people.</summary>
    private async Task<Result<Unit>> RiskStudyAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("risk-assessments/", cancellationToken);
        return await listed.BindAsync(async studies =>
        {
            var existing = IdByRefId(studies, ScopeKind.RiskStudy).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("risk-assessments", existing, write, cancellationToken);
            }

            var folder = folders.FolderOf(declared);
            var perimeter = Field(declared, "perimeter") is { } named
                ? (await folder.BindAsync(f => PerimeterIdAsync(named, f, cancellationToken))).Map(Node)
                : Result.Ok<JsonNode>(JsonValue.Create("")!);
            var matrix = await IdsAsync("risk-matrices/", "urn", [Field(declared, "matrix") ?? ""], "risk matrix", cancellationToken);
            var authors = await ActorsAsync(Split(Field(declared, "authors")), cancellationToken);
            var reviewers = await ActorsAsync(Split(Field(declared, "reviewers")), cancellationToken);
            return await Result.Combine([folder.Map(Node), perimeter, matrix.Map(m => m[0]!.DeepClone()), authors.Map(a => (JsonNode)a), reviewers.Map(r => (JsonNode)r)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["perimeter"] = resolved[1].GetValue<string>() is { Length: > 0 } ? resolved[1].DeepClone() : null,
                    ["risk_matrix"] = resolved[2].DeepClone(),
                    ["status"] = Field(declared, "status"),
                    ["eta"] = Field(declared, "eta"),
                    ["due_date"] = Field(declared, "due_date"),
                    ["authors"] = resolved[3].DeepClone(),
                    ["reviewers"] = resolved[4].DeepClone(),
                };
                Drop(body, write.Change, "eta", "due_date", "perimeter", "status");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "risk-assessments/", body, $"to create risk study {declared.Key}", cancellationToken)
                    : await UpdateAsync("risk-assessments", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>
    /// A risk scenario by its study and <c>ref_id</c>: its systems, threats (by URN), controls in place and planned, ratings by their position on
    /// the matrix (-1 when not rated), treatment and justification.
    /// </summary>
    private async Task<Result<Unit>> RiskScenarioAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var studyRef = write.Declared is { } d ? Field(d, "study") : write.Change.Key.Split('/', 2)[0];
        var studies = await http.ListAllAsync("risk-assessments/", cancellationToken);
        return await studies.BindAsync(async found =>
        {
            if (studyRef is null || IdByRefId(found, ScopeKind.RiskStudy).GetValueOrDefault(studyRef) is not { } study)
            {
                return write.Change.Action == ScopeAction.Delete
                    ? Result.Ok(Unit.Value)
                    : Result.Fail<Unit>(ApiProblems.NotFound("risk study", $"'{studyRef}'"));
            }

            var listed = await http.ListAllAsync($"risk-scenarios/?risk_assessment={Uri.EscapeDataString(study)}", cancellationToken);
            return await listed.BindAsync(async scenarios =>
            {
                var refId = write.Change.Key.Split('/', 2)[^1];
                var existing = scenarios.FirstOrDefault(s => Text(s["ref_id"]) == refId) is { } held ? Id(held) : null;
                if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
                {
                    return await DeleteAsync("risk-scenarios", existing, write, cancellationToken);
                }

                var assets = await IdsAsync("assets/", "name", Split(Field(declared, "systems")), "system", cancellationToken);
                var threats = await IdsAsync("threats/", "urn", Split(Field(declared, "threats")), "threat", cancellationToken);
                var inPlace = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "existing_controls")), "applied control", cancellationToken);
                var planned = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "planned_controls")), "applied control", cancellationToken);
                var exceptions = await IdsAsync("security-exceptions/", "ref_id", Split(Field(declared, "exceptions")), "security exception", cancellationToken);
                return await Result.Combine([assets, threats, inPlace, planned, exceptions]).BindAsync(async resolved =>
                {
                    var body = new JsonObject
                    {
                        ["ref_id"] = refId,
                        ["name"] = Field(declared, "name"),
                        ["description"] = Described(declared, write.Change),
                        ["risk_assessment"] = study,
                        ["assets"] = resolved[0].DeepClone(),
                        ["threats"] = resolved[1].DeepClone(),
                        ["existing_applied_controls"] = resolved[2].DeepClone(),
                        ["applied_controls"] = resolved[3].DeepClone(),
                        ["treatment"] = Field(declared, "treatment"),
                        ["justification"] = Field(declared, "justification"),
                    };
                    if (Field(declared, "exceptions") is not null || write.Change.Fields.Any(f => f.Field == "exceptions"))
                    {
                        body["security_exceptions"] = resolved[4].DeepClone();
                    }

                    foreach (var number in RatingNumbers)
                    {
                        body[number] = declared.Levels.TryGetValue(number, out var level) ? level : -1;
                    }

                    foreach (var number in InherentNumbers.Where(declared.Levels.ContainsKey))
                    {
                        body[number] = declared.Levels[number];
                    }

                    Drop(body, write.Change, "justification");
                    return write.Change.Action == ScopeAction.Create
                        ? await SendAsync(HttpMethod.Post, "risk-scenarios/", body, $"to create risk scenario {declared.Key}", cancellationToken)
                        : await UpdateAsync("risk-scenarios", existing, body, write, cancellationToken);
                });
            });
        });
    }
}
