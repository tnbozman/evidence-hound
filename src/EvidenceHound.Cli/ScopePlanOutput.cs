using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli;

/// <summary>A scope plan as Markdown, for a pull request comment, or as JSON for tools; and an apply's report as JSON.</summary>
internal static class ScopePlanOutput
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Markdown(ScopePlan plan) => Markdown(plan, [], []);

    public static string Markdown(ScopePlan plan, IReadOnlyList<PeriodNote> periods) => Markdown(plan, periods, []);

    /// <summary>
    /// The plan as Markdown, with a table of the audit periods it opens, closes or locks and those awaiting sign-off, and the access review: people
    /// without MFA and service accounts holding more than they need.
    /// </summary>
    public static string Markdown(ScopePlan plan, IReadOnlyList<PeriodNote> periods, IReadOnlyList<AccessNote> access)
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
                var why = drift.Reason switch
                {
                    DriftReason.Edited => "edited in CISO Assistant",
                    DriftReason.Locked => "audit locked: not changed",
                    DriftReason.Outdated => "older library loaded: not upgraded",
                    _ => "not in the scope",
                };
                markdown.Append(CultureInfo.InvariantCulture, $"| {Kind(drift.Kind)} | {Cell(drift.Key)} | {why} | {(drift.Managed ? "yes" : "no")} | {Changed(drift.Fields)} |\n");
            }
        }

        if (periods.Count > 0)
        {
            markdown.Append("\n### Audit periods\n\n| Audit | What happens |\n| --- | --- |\n");
            foreach (var period in periods)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {Cell(period.Audit)} | {Describe(period)} |\n");
            }
        }

        if (access.Count > 0)
        {
            markdown.Append("\n### Access review\n\n| User | Note |\n| --- | --- |\n");
            foreach (var note in access)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {Cell(note.Email)} | {Cell(note.Detail)} |\n");
            }
        }

        return markdown.ToString();
    }

    public static string Json(ScopePlan plan) => Json(plan, [], []);

    /// <summary>The plan as JSON, with the audit periods it opens, closes or locks and those awaiting sign-off, and the access review.</summary>
    public static string Json(ScopePlan plan, IReadOnlyList<PeriodNote> periods, IReadOnlyList<AccessNote> access) =>
        new JsonObject
        {
            ["access"] = new JsonArray([.. access.Select(n => (JsonNode)new JsonObject
            {
                ["user"] = n.Email,
                ["issue"] = n.Issue == AccessIssue.MfaOff ? "mfa_off" : "service_account_excess_role",
                ["detail"] = n.Detail,
            })]),
            ["periods"] = new JsonArray([.. periods.Select(p => (JsonNode)new JsonObject { ["audit"] = p.Audit, ["event"] = Event(p.Event), ["baseline"] = p.Baseline })]),
            ["changes"] = new JsonArray([.. plan.Changes.Select(c => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(c.Kind),
                ["key"] = c.Key,
                ["action"] = c.Action.ToString().ToLowerInvariant(),
                ["fields"] = FieldsJson(c.Fields),
                ["state"] = c.State,
            })]),
            ["drift"] = DriftJson(plan.Drift),
        }.ToJsonString(Indented) + "\n";

    /// <summary>An apply's report: counts, then each change with what happened to it.</summary>
    public static string Json(ScopeApplyReport report) =>
        new JsonObject
        {
            ["written"] = report.Written,
            ["failed"] = report.Failed,
            ["skipped"] = report.Skipped,
            ["not_applied"] = report.NotApplied,
            ["changes"] = new JsonArray([.. report.Changes.Select(c => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(c.Change.Kind),
                ["key"] = c.Change.Key,
                ["action"] = c.Change.Action.ToString().ToLowerInvariant(),
                ["outcome"] = c.Outcome.ToString().ToLowerInvariant(),
                ["detail"] = c.Detail,
            })]),
            ["drift"] = DriftJson(report.Drift),
        }.ToJsonString(Indented) + "\n";

    private static JsonArray DriftJson(IReadOnlyList<ScopeDrift> drift) =>
    [
        .. drift.Select(d => (JsonNode)new JsonObject
        {
            ["kind"] = Kind(d.Kind),
            ["key"] = d.Key,
            ["reason"] = d.Reason switch
            {
                DriftReason.Edited => "edited",
                DriftReason.Locked => "locked",
                DriftReason.Outdated => "outdated",
                _ => "not_in_scope",
            },
            ["managed"] = d.Managed,
            ["fields"] = FieldsJson(d.Fields),
        }),
    ];

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

    private static string Describe(PeriodNote period) => period.Event switch
    {
        PeriodEvent.Opens => period.Baseline is { } baseline ? $"opens, from {Cell(baseline)}" : "opens",
        PeriodEvent.Closes => "closes: in review, awaiting sign-off",
        PeriodEvent.AwaitingSignOff => "awaiting sign-off: add it to the audit file's sign_offs",
        _ => "signed off: done and locked",
    };

    private static string Event(PeriodEvent value) => value switch
    {
        PeriodEvent.Opens => "opens",
        PeriodEvent.Closes => "closes",
        PeriodEvent.AwaitingSignOff => "awaiting_sign_off",
        _ => "signed_off",
    };

    private static string Kind(ScopeKind kind) => kind == ScopeKind.RoleAssignment ? "role_assignment" : kind.ToString().ToLowerInvariant();

    /// <summary>Text that is safe in a Markdown table cell: pipes escaped, line breaks as <c>&lt;br&gt;</c>.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
