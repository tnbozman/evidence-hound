using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli;

/// <summary>A scope plan as Markdown, for a pull request comment, or as JSON for tools.</summary>
internal static class ScopePlanOutput
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Markdown(ScopePlan plan)
    {
        var count = (ScopeAction action) => plan.Changes.Count(c => c.Action == action);
        var markdown = new StringBuilder()
            .Append("## Scope plan\n\n")
            .Append(CultureInfo.InvariantCulture, $"{count(ScopeAction.Create)} to create, {count(ScopeAction.Update)} to update, {count(ScopeAction.Delete)} to delete. ")
            .Append(plan.Drift.Count == 0 ? "No drift.\n" : string.Create(CultureInfo.InvariantCulture, $"{plan.Drift.Count} drifted.\n"));
        if (plan.Changes.Count > 0)
        {
            markdown.Append("\n| Change | Kind | Object | Fields |\n| --- | --- | --- | --- |\n");
            foreach (var change in plan.Changes)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {change.Action} | {Kind(change.Kind)} | {Cell(change.Key)} | {Fields(change)} |\n");
            }
        }

        if (plan.Drift.Count > 0)
        {
            markdown.Append("\n### Drift\n\n| Kind | Object | Why | Managed by hound | Fields |\n| --- | --- | --- | --- | --- |\n");
            foreach (var drift in plan.Drift)
            {
                var why = drift.Reason == DriftReason.Edited ? "edited in CISO Assistant" : "not in the scope";
                markdown.Append(CultureInfo.InvariantCulture, $"| {Kind(drift.Kind)} | {Cell(drift.Key)} | {why} | {(drift.Managed ? "yes" : "no")} | {Changed(drift.Fields)} |\n");
            }
        }

        return markdown.ToString();
    }

    public static string Json(ScopePlan plan) =>
        new JsonObject
        {
            ["changes"] = new JsonArray([.. plan.Changes.Select(c => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(c.Kind),
                ["key"] = c.Key,
                ["action"] = c.Action.ToString().ToLowerInvariant(),
                ["fields"] = FieldsJson(c.Fields),
                ["state"] = c.State,
            })]),
            ["drift"] = new JsonArray([.. plan.Drift.Select(d => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(d.Kind),
                ["key"] = d.Key,
                ["reason"] = d.Reason == DriftReason.Edited ? "edited" : "not_in_scope",
                ["managed"] = d.Managed,
                ["fields"] = FieldsJson(d.Fields),
            })]),
        }.ToJsonString(Indented) + "\n";

    private static JsonArray FieldsJson(IReadOnlyList<FieldChange> fields) =>
        [.. fields.Select(f => (JsonNode)new JsonObject { ["field"] = f.Field, ["old"] = f.Old, ["new"] = f.New })];

    private static string Fields(ScopeChange change) => change switch
    {
        { Action: ScopeAction.Create } => string.Join("<br>", change.Fields.Select(f => $"{f.Field}: {Cell(f.New ?? "")}")),
        { Action: ScopeAction.Update, Fields.Count: 0 } => "mark as managed",
        _ => Changed(change.Fields),
    };

    private static string Changed(IReadOnlyList<FieldChange> fields) =>
        string.Join("<br>", fields.Select(f => $"{f.Field}: {Cell(f.Old ?? "(none)")} → {Cell(f.New ?? "(none)")}"));

    private static string Kind(ScopeKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>Text that is safe in a Markdown table cell: pipes escaped, line breaks as <c>&lt;br&gt;</c>.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
