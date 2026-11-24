using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Core.Scope;

/// <summary>A control to describe, with what it is for: the requirements it serves and the names of their evidence.</summary>
public sealed record SspControlBrief(string RefId, string? Name, string? Owner, IReadOnlyList<string> Systems, IReadOnlyList<SspRequirementBrief> Requirements);

/// <summary>A requirement a control serves: its description from the framework, its status in the plan and its evidence names.</summary>
public sealed record SspRequirementBrief(string Ref, string Description, string? Status, string? Evidence);

/// <summary>
/// The brief for <c>hound ssp suggest</c> (s11-06): the controls with no description that leave an implemented requirement with no
/// implementation statement. hound has no model; the person's own agent drafts from this brief, and <c>hound ssp describe</c> writes each
/// draft to the repo for review. Names only, never evidence content. Pure.
/// </summary>
public static class SspSuggest
{
    private static readonly string NoStatement = "no implementation statement";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IReadOnlyList<SspControlBrief> Brief(SspDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var controls = document.Controls.Where(c => c.RefId.Value is not null).ToDictionary(c => c.RefId.Value!, StringComparer.Ordinal);
        var descriptions = document.Annex.ToDictionary(a => a.Control, a => a.Description, StringComparer.Ordinal);
        var gapControls = document.Requirements
            .Where(r => r.Gaps.Contains(NoStatement))
            .SelectMany(r => SspStatements.Linked(r, controls))
            .Where(c => string.IsNullOrWhiteSpace(c.Description.Value))
            .Select(c => c.RefId.Value!)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            .. gapControls.Order(StringComparer.Ordinal).Select(refId => controls[refId]).Select(c => new SspControlBrief(
                c.RefId.Value!,
                c.Name.Value,
                c.Owner.Value,
                c.Systems,
                [
                    .. document.Requirements.Where(r => SspStatements.Linked(r, controls).Contains(c)).Select(r => new SspRequirementBrief(
                        r.Ref.Value ?? "", descriptions.GetValueOrDefault(r.Ref.Value ?? "", ""), r.Status.Value, r.Evidence.Value)),
                ])),
        ];
    }

    public static string Json(string programme, IReadOnlyList<SspControlBrief> briefs)
    {
        ArgumentNullException.ThrowIfNull(briefs);
        var controls = new JsonArray();
        foreach (var brief in briefs)
        {
            var requirements = new JsonArray();
            foreach (var r in brief.Requirements)
            {
                requirements.Add(new JsonObject { ["ref"] = r.Ref, ["description"] = r.Description, ["status"] = r.Status, ["evidence"] = r.Evidence });
            }

            controls.Add(new JsonObject
            {
                ["ref_id"] = brief.RefId,
                ["name"] = brief.Name,
                ["owner"] = brief.Owner,
                ["systems"] = new JsonArray([.. brief.Systems.Select(s => (JsonNode?)s)]),
                ["requirements"] = requirements,
                ["describe_with"] = $"hound ssp describe --control {brief.RefId} --text \"<description>\"",
            });
        }

        return new JsonObject
        {
            ["programme"] = programme,
            ["instructions"] =
                "Draft one description per control: what the control does, on which systems, in two or three plain sentences an assessor can check. "
                + "Use only the facts here; do not claim more than the requirements' statuses show. Write each with 'hound ssp describe'; a person reviews "
                + "every description in the pull request before 'hound scope apply' sends it to CISO Assistant.",
            ["controls"] = controls,
        }.ToJsonString(Indented) + "\n";
    }
}
