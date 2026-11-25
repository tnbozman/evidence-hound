using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Business impact analyses, their asset assessments and escalation thresholds (s15-04), and a system's objectives.</summary>
internal sealed partial class ScopeWriter
{
    /// <summary>A BIA by <c>ref_id</c>, in its domain and optional perimeter, on a risk matrix found by URN.</summary>
    private async Task<Result<Unit>> BiaAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("business-impact-analysis/", cancellationToken);
        return await listed.BindAsync(async bias =>
        {
            var existing = IdByRefId(bias, ScopeKind.Bia).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("business-impact-analysis", existing, write, cancellationToken);
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
                    ["version"] = Field(declared, "version"),
                    ["status"] = Field(declared, "status"),
                    ["due_date"] = Field(declared, "due_date"),
                    ["authors"] = resolved[3].DeepClone(),
                    ["reviewers"] = resolved[4].DeepClone(),
                };
                Drop(body, write.Change, "perimeter", "version", "status", "due_date");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "business-impact-analysis/", body, $"to create BIA {declared.Key}", cancellationToken)
                    : await UpdateAsync("business-impact-analysis", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>A system's assessment in a BIA, keyed <c>bia/system</c>: dependencies, associated controls, recovery flags and observation.</summary>
    private async Task<Result<Unit>> AssetAssessmentAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var (biaRef, system) = Split2(write.Change.Key);
        var bia = await BiaIdAsync(biaRef, cancellationToken);
        return await bia.BindAsync(async biaId =>
        {
            if (biaId.Length == 0)
            {
                return write.Change.Action == ScopeAction.Delete ? Result.Ok(Unit.Value) : Result.Fail<Unit>(ApiProblems.NotFound("BIA", $"'{biaRef}'"));
            }

            var listed = await http.ListAllAsync($"asset-assessments/?bia={Uri.EscapeDataString(biaId)}", cancellationToken);
            return await listed.BindAsync(async assessments =>
            {
                var existing = assessments.FirstOrDefault(a => Text(a["asset"]?["str"]) == system) is { } held ? Id(held) : null;
                if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
                {
                    return await DeleteAsync("asset-assessments", existing, write, cancellationToken);
                }

                var asset = await IdsAsync("assets/", "name", [system], "system", cancellationToken);
                var dependencies = await IdsAsync("assets/", "name", Split(Field(declared, "dependencies")), "system", cancellationToken);
                var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
                return await Result.Combine([asset.Map(a => a[0]!.DeepClone()), dependencies.Map(d => (JsonNode)d), controls.Map(c => (JsonNode)c)]).BindAsync(async resolved =>
                {
                    var body = new JsonObject
                    {
                        ["bia"] = biaId,
                        ["asset"] = resolved[0].DeepClone(),
                        ["dependencies"] = resolved[1].DeepClone(),
                        ["associated_controls"] = resolved[2].DeepClone(),
                        ["recovery_documented"] = Field(declared, "recovery_documented") == "true",
                        ["recovery_tested"] = Field(declared, "recovery_tested") == "true",
                        ["recovery_targets_met"] = Field(declared, "recovery_targets_met") == "true",
                        ["observation"] = Field(declared, "observation"),
                    };
                    Drop(body, write.Change, "observation");
                    return write.Change.Action == ScopeAction.Create
                        ? await SendAsync(HttpMethod.Post, "asset-assessments/", body, $"to create asset assessment {declared.Key}", cancellationToken)
                        : await UpdateAsync("asset-assessments", existing, body, write, cancellationToken);
                });
            });
        });
    }

    /// <summary>An escalation threshold, keyed <c>bia/system/after</c>: its point in time in seconds and its impact's position on the matrix.</summary>
    private async Task<Result<Unit>> EscalationThresholdAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var cut = write.Change.Key.LastIndexOf('/');
        var assessmentKey = write.Change.Key[..cut];
        var seconds = write.Declared is { } d && int.TryParse(Field(d, "point_in_time"), NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            ? s
            : Duration.TryParse(write.Change.Key[(cut + 1)..], out var after) ? after.Seconds : -1;
        var (biaRef, system) = Split2(assessmentKey);
        var bia = await BiaIdAsync(biaRef, cancellationToken);
        var assessment = await bia.BindAsync(async biaId => biaId.Length == 0
            ? Result.Ok("")
            : (await http.ListAllAsync($"asset-assessments/?bia={Uri.EscapeDataString(biaId)}", cancellationToken))
                .Map(all => all.FirstOrDefault(a => Text(a["asset"]?["str"]) == system) is { } held ? Id(held) : ""));
        return await assessment.BindAsync(async assessmentId =>
        {
            if (assessmentId.Length == 0)
            {
                return write.Change.Action == ScopeAction.Delete ? Result.Ok(Unit.Value) : Result.Fail<Unit>(ApiProblems.NotFound("asset assessment", $"'{assessmentKey}'"));
            }

            var listed = await http.ListAllAsync($"escalation-thresholds/?asset_assessment={Uri.EscapeDataString(assessmentId)}", cancellationToken);
            return await listed.BindAsync(async thresholds =>
            {
                var existing = thresholds.FirstOrDefault(t => t["point_in_time"] is JsonValue v && v.TryGetValue<int>(out var pit) && pit == seconds) is { } held ? Id(held) : null;
                if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
                {
                    return await DeleteAsync("escalation-thresholds", existing, write, cancellationToken);
                }

                var qualifications = await QualificationsAsync(Split(Field(declared, "qualifications")), cancellationToken);
                return await qualifications.BindAsync(async ids =>
                {
                    var body = new JsonObject
                    {
                        ["asset_assessment"] = assessmentId,
                        ["point_in_time"] = seconds,
                        ["quali_impact"] = declared.Levels.TryGetValue("quali_impact", out var level) ? level : -1,
                        ["qualifications"] = ids,
                        ["justification"] = Field(declared, "justification"),
                    };
                    Drop(body, write.Change, "justification");
                    return write.Change.Action == ScopeAction.Create
                        ? await SendAsync(HttpMethod.Post, "escalation-thresholds/", body, $"to create escalation threshold {declared.Key}", cancellationToken)
                        : await UpdateAsync("escalation-thresholds", existing, body, write, cancellationToken);
                });
            });
        });
    }

    /// <summary>The BIA's ID by <c>ref_id</c>, or empty when CISO Assistant holds none.</summary>
    private async Task<Result<string>> BiaIdAsync(string refId, CancellationToken cancellationToken) =>
        (await http.ListAllAsync("business-impact-analysis/", cancellationToken)).Map(all => IdByRefId(all, ScopeKind.Bia).GetValueOrDefault(refId) ?? "");

    /// <summary>Qualifications are CISO Assistant terminologies, found by name.</summary>
    private async Task<Result<JsonArray>> QualificationsAsync(string[] names, CancellationToken cancellationToken) =>
        names.Length == 0
            ? Result.Ok(new JsonArray())
            : (await http.ListAllAsync("terminologies/?field_path=qualifications", cancellationToken)).Bind(all => Resolve(names, IdByField(all, "name"), "qualification"));

    /// <summary>A system's security objectives (0 to 4, enabled) and recovery objectives (in seconds) as CISO Assistant holds them.</summary>
    private static (JsonObject Security, JsonObject Recovery) Objectives(ScopeObject declared)
    {
        var security = new JsonObject();
        foreach (var (name, value) in Pairs(Field(declared, "objectives")))
        {
            security[name] = new JsonObject { ["value"] = int.Parse(value, CultureInfo.InvariantCulture), ["is_enabled"] = true };
        }

        var recovery = new JsonObject();
        foreach (var (name, value) in Pairs(Field(declared, "recovery")))
        {
            recovery[name] = new JsonObject { ["value"] = Duration.TryParse(value, out var duration) ? duration.Seconds : 0 };
        }

        return (new JsonObject { ["objectives"] = security }, new JsonObject { ["objectives"] = recovery });
    }

    private static IEnumerable<(string Name, string Value)> Pairs(string? joined) =>
        Split(joined).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).Select(p => (p[0], p[1]));

    private static (string First, string Second) Split2(string key) => key.Split('/', 2) is [var first, var rest] ? (first, rest) : (key, "");
}
