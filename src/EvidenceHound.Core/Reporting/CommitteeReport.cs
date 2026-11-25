using System.Globalization;
using System.Text;
using static EvidenceHound.Core.Reporting.ReportText;

namespace EvidenceHound.Core.Reporting;

/// <summary>A proposed action for the committee to confirm.</summary>
public sealed record ProposedAction(string Action, string Owner, DateOnly Due, string Source);

/// <summary>
/// s12-05: the monthly security committee report (operational), from the month's snapshot and the previous month's. Pure: every figure
/// comes from the snapshots and links back to its CISO Assistant object. Dates are as at the last day of the month.
/// </summary>
public static class CommitteeReport
{
    /// <summary>Exceptions and risk acceptances expiring within this many days of the month's end are listed as expiring.</summary>
    public const int ExpiringWindowDays = 60;

    /// <summary>A collector whose last run is older than this, at the month's end, is flagged as stale.</summary>
    public const int StaleAfterDays = 7;

    public static string Render(PostureSnapshot month, PostureSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(month);
        var view = new View(month, previous);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# Security committee report: {MonthName(month.Period)}\n\n")
            .Append(CultureInfo.InvariantCulture, $"Period {Date(month.Period.Start)} to {Date(month.Period.End)}, as at the end of the month. Snapshot taken {month.TakenAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC")
            .Append(previous is null ? "; there is no previous snapshot to compare with.\n" : $"; compared with {previous.Period}.\n");
        Section(text, "1. Summary", Summary(view));
        Section(text, "2. Collector health", Collectors(view));
        Section(text, "3. Control status changes", Changes(view));
        Section(text, "4. Exceptions and risk acceptances", Acceptances(view));
        Section(text, "5. Remediation", Remediation(view));
        Section(text, "6. Incidents", Incidents(view));
        Section(text, "7. Manual evidence and third parties", DueItems(view));
        Section(text, "8. Actions", Actions(view));
        return text.ToString();
    }

    /// <summary>The actions the report proposes, in the order of section 8.</summary>
    public static IReadOnlyList<ProposedAction> ProposedActions(PostureSnapshot month, PostureSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(month);
        return new View(month, previous).Actions;
    }

    private static void Section(StringBuilder text, string heading, string body) => text.Append(CultureInfo.InvariantCulture, $"\n## {heading}\n\n{body.TrimEnd('\n')}\n");

    private static string Summary(View view)
    {
        var lines = new List<string>();
        if (view.Previous is not null)
        {
            var changed = view.Diff!.Requirements;
            var better = changed.Count(c => Rank(c.To) > Rank(c.From));
            var worse = changed.Count(c => Rank(c.To) < Rank(c.From));
            lines.Add(changed.Count == 0
                ? "Requirement results changed since last month: none."
                : $"Requirement results changed since last month: {Number(changed.Count)} ({Number(better)} better, {Number(worse)} worse).");
        }

        lines.Add($"Exceptions and risk acceptances expiring within {Number(ExpiringWindowDays)} days: {Number(view.Expiring.Count)}. Expired this month: {Number(view.Expired.Count)}.");
        lines.Add($"Remediation items overdue: {Number(view.OverdueRemediation.Count)}.");
        var secondMonth = view.Due.Count(d => d.SecondMonth);
        lines.Add($"Manual evidence and supplier reviews overdue: {Number(view.Due.Count(d => d.Overdue))} ({Number(secondMonth)} for a second month).");
        lines.Add(view.Actions.Count == 0
            ? "Decisions needed: None."
            : $"Decisions needed: confirm, change or drop the proposed actions in section 8 ({Number(view.Actions.Count)}).");
        return string.Concat(lines.Select(l => $"- {l}\n"));
    }

    private static string Collectors(View view)
    {
        if (view.Month.Collectors.Count == 0)
        {
            return None + " No collector has run on this deployment.";
        }

        var text = new StringBuilder("| Collector | Last run (UTC) | Outcome | Checks passed | Checks failed | Problems |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var c in view.Month.Collectors)
        {
            var stale = Stale(c, view.Month.Period) ? " (stale)" : "";
            text.Append(CultureInfo.InvariantCulture, $"| {Cell(c.Collector)} | {(c.LastRun is { } run ? run.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never")}{stale} | {Cell(c.Outcome)} | {Number(c.ChecksPassed)} | {Number(c.ChecksFailed)} | {Cell(string.Join(" ", c.Problems))} |\n");
        }

        return !view.Month.Collectors.Any(c => Stale(c, view.Month.Period)) ? text.ToString() : text.Append(CultureInfo.InvariantCulture, $"\nStale: no run in the {Number(StaleAfterDays)} days before the end of the month, so its evidence may be past its freshness window.\n").ToString();
    }

    private static string Changes(View view)
    {
        if (view.Month.Programmes.Count == 0 && view.Month.Controls.Count == 0)
        {
            return None;
        }

        var text = new StringBuilder();
        var changes = view.Diff?.Requirements ?? [];
        foreach (var programme in view.Month.Programmes)
        {
            text.Append(CultureInfo.InvariantCulture, $"### {programme.Name}\n\nAudit of record: {Linked(programme.Audit, programme.Link)}{(programme.Closed ? " (closed)" : "")}.\n\n");
            text.Append(GroupTable(programme)).Append('\n');
            var mine = changes.Where(c => c.Programme == programme.Id).ToList();
            if (view.Previous is null)
            {
                text.Append("No previous snapshot: changes start next month.\n\n");
            }
            else if (mine.Count == 0)
            {
                text.Append("No result changed.\n\n");
            }
            else
            {
                foreach (var parent in programme.Parents.Where(p => mine.Any(c => c.ParentUrn == p.Urn)))
                {
                    text.Append(CultureInfo.InvariantCulture, $"#### {parent.Name}\n\n");
                    foreach (var change in mine.Where(c => c.ParentUrn == parent.Urn))
                    {
                        var link = programme.Requirements.FirstOrDefault(r => r.Urn == change.Urn)?.Link;
                        text.Append(CultureInfo.InvariantCulture, $"- {Linked(change.RefId ?? change.Urn, link)}: {(change.From is { } from ? Result(from) : "new row")} → {Result(change.To)}\n");
                    }

                    text.Append('\n');
                }
            }

            if (programme.Register is { } register)
            {
                text.Append(CultureInfo.InvariantCulture, $"Register: {RegisterLine(register)}\n\n");
            }
        }

        text.Append("### Applied controls\n\n");
        var controls = view.Diff?.Controls ?? [];
        if (controls.Count == 0)
        {
            text.Append(view.Previous is null ? "No previous snapshot: changes start next month.\n" : "No applied control changed status.\n");
        }

        foreach (var change in controls)
        {
            var control = view.Month.Controls.First(c => c.RefId == change.RefId);
            text.Append(CultureInfo.InvariantCulture, $"- {Linked(Name(change.RefId, change.Name), control.Link)}: {change.From ?? "new"} → {change.To}\n");
        }

        return text.ToString();
    }

    private static string GroupTable(ProgrammePosture programme)
    {
        var text = new StringBuilder("| Group | Compliant | Partially compliant | Non-compliant | Not assessed | Not applicable |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var count in programme.Counts.Where(c => c.ParentUrn is null))
        {
            var name = programme.Groups.FirstOrDefault(g => g.RefId == count.Group)?.Name ?? count.Group;
            text.Append(CultureInfo.InvariantCulture, $"| {Cell(name)} | {Number(count.Compliant)} | {Number(count.PartiallyCompliant)} | {Number(count.NonCompliant)} | {Number(count.NotAssessed)} | {Number(count.NotApplicable)} |\n");
        }

        return text.ToString();
    }

    internal static string RegisterLine(RegisterSnapshot register)
    {
        var counts = register.Counts.GroupBy(c => c.Field).Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => $"{c.Value} {Number(c.Count)}"))}");
        var line = $"{Number(register.Total)} {register.Of}";
        line += register.Counts.Count > 0 ? $". {string.Join("; ", counts)}." : ".";
        if (register.Added.Count > 0)
        {
            line += $" Added: {string.Join(", ", register.Added)}.";
        }

        if (register.Retired.Count > 0)
        {
            line += $" Retired: {string.Join(", ", register.Retired)}.";
        }

        return line;
    }

    private static string Acceptances(View view)
    {
        static string Line(AcceptanceItem a) =>
            $"- {Linked(a.RefId, a.Link)} {a.Name} ({Kind(a.Kind)}): {a.State}{(a.AcceptedBy is null ? "" : $" by {a.AcceptedBy}")} on {Date(a.AcceptedOn)}, expiry {Date(a.Expires)}.\n";

        static string List(IReadOnlyList<AcceptanceItem> items) => items.Count == 0 ? None + "\n" : string.Concat(items.Select(Line));

        return $"### New\n\n{List(view.NewAcceptances)}\n### Expiring within {Number(ExpiringWindowDays)} days\n\n{List(view.Expiring)}\n### Expired\n\n{List(view.Expired)}";
    }

    private static string Remediation(View view)
    {
        var open = view.Month.Remediation.Where(r => r.Open).ToList();
        var completed = view.CompletedRemediation;
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Open: {Number(open.Count)}. Completed this month: {Number(completed.Count)}")
            .Append(completed.Count == 0 ? "" : $" ({string.Join(", ", completed.Select(r => Linked(r.RefId, r.Link)))})")
            .Append(CultureInfo.InvariantCulture, $". Overdue: {Number(view.OverdueRemediation.Count)}.\n\n### Overdue\n\n");
        text.Append(view.OverdueRemediation.Count == 0 ? None + "\n" : RemediationTable(view.OverdueRemediation, view.Month.Period.End));
        text.Append("\n### Open, by owner\n\n");
        var notOverdue = open.Except(view.OverdueRemediation).OrderBy(r => r.Owner ?? "~", StringComparer.Ordinal).ThenBy(r => r.Due).ToList();
        text.Append(notOverdue.Count == 0 ? None + "\n" : RemediationTable(notOverdue, view.Month.Period.End));
        return text.ToString();
    }

    private static string RemediationTable(IEnumerable<RemediationItem> items, DateOnly asAt)
    {
        var text = new StringBuilder("| Item | Owner | Due | Days overdue | Status |\n| --- | --- | --- | --- | --- |\n");
        foreach (var r in items)
        {
            var late = r.Due is { } due && due < asAt ? Number(asAt.DayNumber - due.DayNumber) : "-";
            text.Append(CultureInfo.InvariantCulture, $"| {Linked(Name(r.RefId, r.Name), r.Link)} | {Cell(r.Owner)} | {Date(r.Due)} | {late} | {Cell(r.Status)} |\n");
        }

        return text.ToString();
    }

    private static string Incidents(View view) => IncidentsOpenedOrClosed(view) + OverdueReports(view);

    /// <summary>Each report an incident owes that is past its deadline and not recorded as made (s15-07), soonest first.</summary>
    private static string OverdueReports(View view)
    {
        if (view.OverdueReports.Count == 0)
        {
            return "";
        }

        var text = new StringBuilder("\n### Reports overdue\n\nRecord each report you make as a timeline entry on the incident: 'Reported: <id>, <their reference>'.\n\n")
            .Append("| Incident | Report | To | Due |\n| --- | --- | --- | --- |\n");
        foreach (var (incident, report) in view.OverdueReports)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Linked(Name(incident.RefId, incident.Name), incident.Link)} | {Cell($"{report.Id}: {report.Name}")} | {Cell(report.ReportTo)} | {report.Due.ToUniversalTime():yyyy-MM-dd HH:mm} UTC |\n");
        }

        return text.ToString();
    }

    private static string IncidentsOpenedOrClosed(View view)
    {
        var period = view.Month.Period;
        var items = view.Month.Incidents
            .Where(i => (i.ReportedOn is { } r && period.Contains(r)) || (i.ClosedOn is { } c && period.Contains(c)))
            .OrderBy(i => i.ReportedOn)
            .ToList();
        if (items.Count == 0)
        {
            return None + " No incident was opened or closed in CISO Assistant this month.\n";
        }

        var opened = items.Count(i => i.ReportedOn is { } r && period.Contains(r));
        var closed = items.Count(i => i.ClosedOn is { } c && period.Contains(c));
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Opened: {Number(opened)}. Closed: {Number(closed)}.\n\n")
            .Append("| Incident | Severity | Reported | Closed | Status |\n| --- | --- | --- | --- | --- |\n");
        foreach (var i in items)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Linked(Name(i.RefId, i.Name), i.Link)} | {Cell(i.Severity)} | {Date(i.ReportedOn)} | {(i.ClosedOn is null ? "-" : Date(i.ClosedOn))} | {Cell(i.Status)} |\n");
        }

        return text.ToString();
    }

    private static string DueItems(View view)
    {
        if (view.Due.Count == 0)
        {
            return None + $" Nothing is overdue or due in the next 30 days.";
        }

        var text = new StringBuilder("| Item | Kind | Owner | Due | State |\n| --- | --- | --- | --- | --- |\n");
        foreach (var d in view.Due)
        {
            var state = d.SecondMonth ? "**overdue, second month**" : d.Overdue ? "overdue" : "due";
            text.Append(CultureInfo.InvariantCulture, $"| {Linked(d.Item.Name, d.Item.Link)} | {(d.Item.Kind == DueKind.ManualEvidence ? "manual evidence" : "supplier review")} | {Cell(d.Item.Owner)} | {Date(d.Item.Due)} | {state} |\n");
        }

        return text.Append("\nOverdue items are carried into section 8 until they are completed: the committee is the escalation after the reminder emails.\n").ToString();
    }

    private static string Actions(View view)
    {
        if (view.Actions.Count == 0)
        {
            return None;
        }

        var text = new StringBuilder("Proposed for the committee to confirm, change or drop. Record the confirmed ones in CISO Assistant as tasks.\n\n| # | Action | Owner | Due | From |\n| --- | --- | --- | --- | --- |\n");
        var n = 0;
        foreach (var a in view.Actions)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Number(++n)} | {Cell(a.Action)} | {Cell(a.Owner)} | {Date(a.Due)} | {Cell(a.Source)} |\n");
        }

        return text.ToString();
    }

    private static bool Stale(CollectorHealth collector, ReportingPeriod period) =>
        collector.LastRun is not { } run || DateOnly.FromDateTime(run.UtcDateTime).AddDays(StaleAfterDays) < period.End;

    private static string Name(string refId, string name) => string.IsNullOrEmpty(refId) ? name : $"{refId} {name}";

    private sealed record DueRow(DueItem Item, bool Overdue, bool SecondMonth);

    /// <summary>Everything the sections need, worked out once from the two snapshots.</summary>
    private sealed class View
    {
        public View(PostureSnapshot month, PostureSnapshot? previous)
        {
            Month = month;
            Previous = previous;
            Diff = previous is null ? null : Snapshots.Diff(previous, month);
            var period = month.Period;
            var end = period.End;
            NewAcceptances = [.. month.Acceptances.Where(a => a.Active && a.AcceptedOn is { } on && period.Contains(on))];
            Expiring = [.. month.Acceptances.Where(a => a.Active && a.Expires is { } e && e > end && e <= end.AddDays(ExpiringWindowDays)).OrderBy(a => a.Expires)];
            Expired = [.. month.Acceptances.Where(a => a.Expires is { } e && period.Contains(e)).OrderBy(a => a.Expires)];
            OverdueRemediation = [.. month.Remediation.Where(r => r.Open && r.Due is { } d && d < end).OrderBy(r => r.Due)];
            CompletedRemediation = previous is null
                ? [.. month.Remediation.Where(r => r.ClosedOn is { } c && period.Contains(c))]
                : [.. previous.Remediation.Where(r => r.Open && !month.Remediation.Any(m => m.RefId == r.RefId && m.Open))];
            var overdueBefore = previous?.Due.Where(d => !d.Done && d.Due < previous.Period.End).Select(d => (d.Kind, d.Name)).ToHashSet() ?? [];
            Due =
            [
                .. month.Due
                    .Where(d => !d.Done && d.Due <= end.AddDays(30))
                    .Select(d => new DueRow(d, d.Due < end, d.Due < end && overdueBefore.Contains((d.Kind, d.Name))))
                    .OrderByDescending(d => d.SecondMonth)
                    .ThenByDescending(d => d.Overdue)
                    .ThenBy(d => d.Item.Due),
            ];
            OverdueReports = [.. Month.Incidents.SelectMany(i => i.Reports.Where(r => r.Status == "overdue").Select(r => (i, r))).OrderBy(x => x.r.Due).ThenBy(x => x.i.RefId, StringComparer.Ordinal)];
            Actions = ProposeActions();
        }

        public PostureSnapshot Month { get; }

        public PostureSnapshot? Previous { get; }

        public SnapshotDiff? Diff { get; }

        public IReadOnlyList<AcceptanceItem> NewAcceptances { get; }

        public IReadOnlyList<AcceptanceItem> Expiring { get; }

        public IReadOnlyList<AcceptanceItem> Expired { get; }

        public IReadOnlyList<RemediationItem> OverdueRemediation { get; }

        public IReadOnlyList<RemediationItem> CompletedRemediation { get; }

        public IReadOnlyList<DueRow> Due { get; }

        public List<ProposedAction> Actions { get; }

        public IReadOnlyList<(IncidentItem Incident, IncidentReport Report)> OverdueReports { get; }

        private List<ProposedAction> ProposeActions()
        {
            var end = Month.Period.End;
            var soon = end.AddDays(14);
            var actions = new List<ProposedAction>();
            actions.AddRange(Due.Where(d => d.Overdue).Select(d => new ProposedAction(
                $"Complete {d.Item.Name}{(d.SecondMonth ? " (overdue a second month)" : "")}", d.Item.Owner ?? "unassigned", soon, "section 7")));
            actions.AddRange(OverdueRemediation.Select(r => new ProposedAction($"Complete {Name(r.RefId, r.Name)} or agree a new due date", r.Owner ?? "unassigned", soon, "section 5")));
            actions.AddRange(Expired.Where(a => a.Active).Select(a => new ProposedAction($"Renew or close {a.RefId} {a.Name}: it expired on {Date(a.Expires)}", a.AcceptedBy ?? "unassigned", soon, "section 4")));
            actions.AddRange(Expiring.Select(a => new ProposedAction($"Review {a.RefId} {a.Name} before it expires", a.AcceptedBy ?? "unassigned", a.Expires!.Value, "section 4")));
            actions.AddRange(Month.Collectors.Where(c => c.Outcome is not "Succeeded" || Stale(c, Month.Period)).Select(c => new ProposedAction(
                $"Fix collector {c.Collector}{(c.Problems.Count > 0 ? ": " + c.Problems[0] : "")}", "Collector operator", end.AddDays(7), "section 2")));
            actions.AddRange(Month.Warnings.Select(w => new ProposedAction($"Resolve {w.Code}: {w.Message}", "Cyber GRC lead", end.AddDays(30), "scope warning")));
            actions.AddRange(OverdueReports.Select(o => new ProposedAction(
                $"Make the {o.Report.Name} for {o.Incident.RefId} ({o.Report.ReportTo}), overdue since {Date(DateOnly.FromDateTime(o.Report.Due.UtcDateTime))}, and record it on the incident",
                "Cyber GRC lead", Month.Period.End, "section 6")));
            return actions;
        }
    }
}
