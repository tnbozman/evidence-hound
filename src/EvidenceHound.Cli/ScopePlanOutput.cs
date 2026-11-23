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

    public static string Markdown(ScopePlan plan) => Markdown(plan, PlanNotes.None);

    public static string Markdown(ScopePlan plan, IReadOnlyList<PeriodNote> periods) => Markdown(plan, PlanNotes.None with { Periods = periods });

    /// <summary>
    /// The plan as Markdown, with a table of the audit periods it opens, closes or locks and those awaiting sign-off, the access review (people
    /// without MFA and service accounts holding more than they need), and, when asked, the settings the scope leaves to CISO Assistant.
    /// </summary>
    public static string Markdown(ScopePlan plan, PlanNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var (periods, access) = (notes.Periods, notes.Access);
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

        AppendCompliance(markdown, notes.Compliance);

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

        if (notes.Suppliers.Count > 0)
        {
            markdown.Append("\n### Suppliers\n\nRead from CISO Assistant, where the GRC team concludes supplier assessments.\n\n| Supplier | System | Note |\n| --- | --- | --- |\n");
            foreach (var note in notes.Suppliers)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {Cell(note.Supplier)} | {Cell(note.System ?? "")} | {Cell(note.Detail)} [{note.Code}] |\n");
            }
        }

        if (notes.Gaps.Count > 0)
        {
            markdown.Append("\n### Capability gaps\n\nMitigations scope/capabilities.yaml says no product provides: no system's audit answers these requirements, and none fails for them.\n\n| Mitigation | Requirements | Ref IDs |\n| --- | --- | --- |\n");
            foreach (var gap in notes.Gaps)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {Cell(gap.Mitigation)} | {gap.Requirements.Count} | {Cell(string.Join(", ", gap.Requirements))} |\n");
            }
        }

        if (notes.Unmanaged.Count > 0)
        {
            markdown.Append("\n### Unmanaged settings\n\nCISO Assistant's own: settings.yaml does not name them, so applying leaves them as they are.\n\n| Kind | Name | Value |\n| --- | --- | --- |\n");
            foreach (var setting in notes.Unmanaged)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"| {Kind(setting.Kind)} | {Cell(setting.Key)} | {Cell(setting.Fields.GetValueOrDefault("value") ?? "")} |\n");
            }
        }

        return markdown.ToString();
    }

    public static string Json(ScopePlan plan) => Json(plan, PlanNotes.None);

    /// <summary>The plan as JSON, with the audit periods it opens, closes or locks and those awaiting sign-off, the access review and any unmanaged settings.</summary>
    public static string Json(ScopePlan plan, PlanNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var (periods, access) = (notes.Periods, notes.Access);
        return new JsonObject
        {
            ["unmanaged_settings"] = new JsonArray([.. notes.Unmanaged.Select(s => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(s.Kind),
                ["name"] = s.Key,
                ["value"] = s.Fields.GetValueOrDefault("value"),
            })]),
            ["access"] = new JsonArray([.. access.Select(n => (JsonNode)new JsonObject
            {
                ["user"] = n.Email,
                ["issue"] = n.Issue == AccessIssue.MfaOff ? "mfa_off" : "service_account_excess_role",
                ["detail"] = n.Detail,
            })]),
            ["periods"] = new JsonArray([.. periods.Select(p => (JsonNode)new JsonObject { ["audit"] = p.Audit, ["event"] = Event(p.Event), ["baseline"] = p.Baseline })]),
            ["gaps"] = new JsonArray([.. notes.Gaps.Select(g => (JsonNode)new JsonObject
            {
                ["mitigation"] = g.Mitigation,
                ["requirements"] = new JsonArray([.. g.Requirements.Select(r => (JsonNode)JsonValue.Create(r))]),
            })]),
            ["suppliers"] = new JsonArray([.. notes.Suppliers.Select(n => (JsonNode)new JsonObject { ["code"] = n.Code, ["supplier"] = n.Supplier, ["system"] = n.System, ["detail"] = n.Detail })]),
            ["changes"] = new JsonArray([.. plan.Changes.Select(c => (JsonNode)new JsonObject
            {
                ["kind"] = Kind(c.Kind),
                ["key"] = c.Key,
                ["action"] = c.Action.ToString().ToLowerInvariant(),
                ["fields"] = FieldsJson(c.Fields),
                ["state"] = c.State,
            })]),
            ["drift"] = DriftJson(plan.Drift),
            ["compliance"] = ComplianceJson(notes.Compliance),
        }.ToJsonString(Indented) + "\n";
    }

    /// <summary>An apply's report: counts, then each change with what happened to it, and the effect on compliance it planned.</summary>
    public static string Json(ScopeApplyReport report, IReadOnlyList<AuditImpact> compliance) =>
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
            ["compliance"] = ComplianceJson(compliance),
        }.ToJsonString(Indented) + "\n";

    /// <summary>
    /// The plan's effect on each audit, after the change table: compliant counts per implementation group, then each row whose result
    /// changes, regressions first and marked ⚠. Nothing when no result changes.
    /// </summary>
    private static void AppendCompliance(StringBuilder markdown, IReadOnlyList<AuditImpact> compliance)
    {
        if (compliance.Count == 0)
        {
            return;
        }

        markdown.Append("\n### Changes to compliance\n");
        foreach (var audit in compliance)
        {
            var regressions = audit.Regressions switch
            {
                0 => "no regressions",
                1 => "1 regression ⚠",
                var n => string.Create(CultureInfo.InvariantCulture, $"{n} regressions ⚠"),
            };
            markdown.Append(CultureInfo.InvariantCulture, $"\n**{Cell(audit.Audit)}**: {audit.Rows.Count} result changes, {regressions}.\n\n");
            foreach (var group in audit.Groups)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"- {Cell(group.Name)}: {group.Before} → {group.After} compliant of {group.Total}\n");
            }

            markdown.Append("\n|  | Requirement | Title | Strategy | Result | Why |\n| --- | --- | --- | --- | --- | --- |\n");
            foreach (var row in audit.Rows)
            {
                markdown.Append(CultureInfo.InvariantCulture,
                    $"| {(row.Regresses ? "⚠" : "")} | {Cell(row.Requirement)} | {Cell(row.Title)} | {Cell(row.Parent ?? "(none)")} | {row.Old} → {row.New} | {row.Why} |\n");
            }
        }
    }

    private static JsonArray ComplianceJson(IReadOnlyList<AuditImpact> compliance) =>
    [
        .. compliance.Select(a => (JsonNode)new JsonObject
        {
            ["audit"] = a.Audit,
            ["groups"] = new JsonArray([.. a.Groups.Select(g => (JsonNode)new JsonObject { ["group"] = g.Group, ["name"] = g.Name, ["before"] = g.Before, ["after"] = g.After, ["total"] = g.Total })]),
            ["rows"] = new JsonArray([.. a.Rows.Select(r => (JsonNode)new JsonObject
            {
                ["requirement"] = r.Requirement,
                ["title"] = r.Title,
                ["parent"] = r.Parent,
                ["old"] = r.Old,
                ["new"] = r.New,
                ["why"] = r.Why,
                ["regresses"] = r.Regresses,
            })]),
        }),
    ];

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

    internal static string Kind(ScopeKind kind) => kind switch
    {
        ScopeKind.FeatureFlag => "feature_flag",
        ScopeKind.EntityAssessment => "supplier_assessment",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Text that is safe in a Markdown table cell: pipes escaped, line breaks as <c>&lt;br&gt;</c>.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}

/// <summary>What a plan shows besides its changes and drift: audit periods, the access review, and the unmanaged settings when asked.</summary>
internal sealed record PlanNotes(IReadOnlyList<PeriodNote> Periods, IReadOnlyList<AccessNote> Access)
{
    public static PlanNotes None { get; } = new([], []);

    public IReadOnlyList<ScopeObject> Unmanaged { get; init; } = [];

    /// <summary>The plan's effect on each audit's compliance; empty when no result changes.</summary>
    public IReadOnlyList<AuditImpact> Compliance { get; init; } = [];

    /// <summary>Lapsed supplier approvals and tiers that differ from the forced one.</summary>
    public IReadOnlyList<SupplierNote> Suppliers { get; init; } = [];

    /// <summary>Mitigations the capability map says no product provides.</summary>
    public IReadOnlyList<CapabilityGap> Gaps { get; init; } = [];
}
