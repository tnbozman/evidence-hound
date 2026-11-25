using System.Globalization;
using System.Text;
using EvidenceHound.Core.Mapping;
using static EvidenceHound.Core.Reporting.ReportText;

namespace EvidenceHound.Core.Reporting;

/// <summary>A programme's compliance position, proposed for the authorising official to confirm.</summary>
public enum CompliancePosition
{
    OnTrack,
    OnTrackWithConditions,
    OffTrack,
}

/// <summary>
/// s12-06: the quarterly board report (governance), from the quarter-end snapshots (the snapshot of each quarter's last month). Pure.
/// The body holds no operational detail (no collector failures or task lists): that is in the appendix and the committee reports.
/// </summary>
public static class BoardReport
{
    /// <summary>How many quarters the posture trend shows.</summary>
    public const int TrendQuarters = 4;

    /// <summary>How many risks the key risks section shows.</summary>
    public const int KeyRisks = 5;

    /// <summary>A non-compliant share above this puts a programme off track.</summary>
    public const double OffTrackNonCompliantShare = 0.25;

    /// <summary>A not-assessed share above this keeps a programme from being simply on track.</summary>
    public const double OnTrackNotAssessedShare = 0.10;

    /// <summary>An acceptance valid for longer than this many months after it was made needs the board's endorsement.</summary>
    public const int EndorseAfterMonths = 12;

    public static string Render(ReportingPeriod quarter, IReadOnlyList<PostureSnapshot> quarterEnds)
    {
        ArgumentNullException.ThrowIfNull(quarter);
        ArgumentNullException.ThrowIfNull(quarterEnds);
        if (quarter.Kind != PeriodKind.Quarter)
        {
            throw new ArgumentException("The board report is quarterly.", nameof(quarter));
        }

        var trend = quarterEnds
            .Where(s => s.Period.End <= quarter.End && s.Period.End.Month % 3 == 0)
            .OrderBy(s => s.Period.End)
            .TakeLast(TrendQuarters)
            .ToList();
        var current = trend.LastOrDefault(s => s.Period.End == quarter.End)
            ?? throw new ArgumentException($"No snapshot for the end of {quarter}.", nameof(quarterEnds));
        var previous = trend.Count > 1 ? trend[^2] : null;
        var positions = current.Programmes.Select(p => Position(p, previous?.Programmes.FirstOrDefault(x => x.Id == p.Id))).ToList();
        var endorse = current.Acceptances.Where(a => a.AcceptedOn is { } on && quarter.Contains(on) && NeedsEndorsement(a)).ToList();

        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# Board report: {QuarterName(quarter)}\n\n")
            .Append(CultureInfo.InvariantCulture, $"As at {Date(quarter.End)}. Figures come from the quarter-end snapshots in CISO Assistant; the monthly committee reports hold the operational detail.\n");
        Section(text, "1. Compliance position", PositionSection(positions));
        Section(text, "2. Posture", PostureSection(current, trend));
        Section(text, "3. Key risks", RisksSection(current));
        Section(text, "4. Risk-acceptance decisions", AcceptanceSection(current, quarter));
        Section(text, "5. Assurance", AssuranceSection(current, quarter));
        Section(text, "6. Decisions and asks of the board", AsksSection(positions, endorse));
        Section(text, "Appendix: operational detail", Appendix(current, quarter));
        return text.ToString();
    }

    private sealed record ProgrammePosition(ProgrammePosture Programme, CompliancePosition Position, IReadOnlyList<string> Reasons);

    private static void Section(StringBuilder text, string heading, string body) => text.Append(CultureInfo.InvariantCulture, $"\n## {heading}\n\n{body.TrimEnd('\n')}\n");

    private static string QuarterName(ReportingPeriod quarter) => $"{quarter} ({quarter.Start.ToString("MMMM", CultureInfo.InvariantCulture)} to {quarter.End.ToString("MMMM yyyy", CultureInfo.InvariantCulture)})";

    private static string QuarterOf(PostureSnapshot snapshot) => new ReportingPeriod(PeriodKind.Quarter, snapshot.Period.End.Year, ((snapshot.Period.End.Month - 1) / 3) + 1).ToString();

    private static ProgrammePosition Position(ProgrammePosture programme, ProgrammePosture? before)
    {
        var group = Last(programme.Groups)?.RefId;
        var rows = programme.Requirements.Where(r => group is null || r.Groups.Contains(group)).ToList();
        var total = rows.Count;
        var compliant = rows.Count(r => r.Result == ComplianceResult.Compliant);
        var nonCompliant = rows.Count(r => r.Result == ComplianceResult.NonCompliant);
        var notAssessed = rows.Count(r => r.Result == ComplianceResult.NotAssessed);
        var earlier = before?.Requirements.ToDictionary(r => r.Urn, r => r.Result, StringComparer.Ordinal) ?? [];
        var worse = rows.Where(r => earlier.TryGetValue(r.Urn, out var was) && Rank(r.Result) < Rank(was)).ToList();
        var groupName = Last(programme.Groups)?.Name ?? "all groups";
        var reasons = new List<string> { $"{Number(compliant)} of {Number(total)} requirements compliant at {groupName}." };
        if (worse.Count > 0)
        {
            reasons.Add($"{Number(worse.Count)} got worse since last quarter ({string.Join(", ", worse.Select(w => w.RefId ?? w.Urn))}).");
        }

        if (nonCompliant > 0)
        {
            reasons.Add($"{Number(nonCompliant)} non-compliant.");
        }

        if (notAssessed > 0)
        {
            reasons.Add($"{Number(notAssessed)} not yet assessed.");
        }

        var position = worse.Count > 0 || (total > 0 && (double)nonCompliant / total > OffTrackNonCompliantShare)
            ? CompliancePosition.OffTrack
            : nonCompliant == 0 && (total == 0 || (double)notAssessed / total <= OnTrackNotAssessedShare)
                ? CompliancePosition.OnTrack
                : CompliancePosition.OnTrackWithConditions;
        return new ProgrammePosition(programme, position, reasons);
    }

    private static string Name(CompliancePosition position) => position switch
    {
        CompliancePosition.OnTrack => "on track",
        CompliancePosition.OnTrackWithConditions => "on track with conditions",
        _ => "off track",
    };

    private static string PositionSection(IReadOnlyList<ProgrammePosition> positions)
    {
        if (positions.Count == 0)
        {
            return None + " No programme is declared in the scope.";
        }

        var text = new StringBuilder("Proposed by management for the authorising official to confirm.\n\n");
        foreach (var p in positions)
        {
            text.Append(CultureInfo.InvariantCulture, $"- **{p.Programme.Name}: {Name(p.Position)}**. {string.Join(" ", p.Reasons)} {Standing.Of(p.Programme).Text()} Audit of record: {Linked(p.Programme.Audit, p.Programme.Link)}.\n");
        }

        return text.ToString();
    }

    private static string PostureSection(PostureSnapshot current, IReadOnlyList<PostureSnapshot> trend)
    {
        if (current.Programmes.Count == 0)
        {
            return None;
        }

        var text = new StringBuilder("Compliant requirements out of those assessed in each group, per parent, at the end of each quarter.\n");
        foreach (var programme in current.Programmes)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### {programme.Name}\n");
            var history = trend.Select(s => (Quarter: QuarterOf(s), Programme: s.Programmes.FirstOrDefault(p => p.Id == programme.Id))).ToList();
            foreach (var group in programme.Groups)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n| {Cell(group.Name)} | {string.Join(" | ", history.Select(h => h.Quarter))} |\n| --- |{string.Concat(history.Select(_ => " --- |"))}\n");
                foreach (var parent in programme.Parents.Where(p => programme.Counts.Any(c => c.Group == group.RefId && c.ParentUrn == p.Urn)))
                {
                    text.Append(CultureInfo.InvariantCulture, $"| {Cell(parent.Name)} | {string.Join(" | ", history.Select(h => Share(h.Programme, group.RefId, parent.Urn)))} |\n");
                }

                text.Append(CultureInfo.InvariantCulture, $"| **All** | {string.Join(" | ", history.Select(h => Share(h.Programme, group.RefId, null)))} |\n");
            }

            if (programme.Register is { } register)
            {
                text.Append(CultureInfo.InvariantCulture, $"\nRegister: {CommitteeReport.RegisterLine(register)}\n");
            }
        }

        return text.ToString();
    }

    private static string Share(ProgrammePosture? programme, string group, string? parent) =>
        programme?.Counts.FirstOrDefault(c => c.Group == group && c.ParentUrn == parent) is { } count
            ? $"{Number(count.Compliant)}/{Number(count.Total - count.NotApplicable)}"
            : "-";

    private static string RisksSection(PostureSnapshot current)
    {
        var top = current.Risks.OrderByDescending(r => r.Rank).ThenBy(r => r.RefId, StringComparer.Ordinal).Take(KeyRisks).ToList();
        if (top.Count == 0)
        {
            return None + " No risk scenario is recorded in CISO Assistant.";
        }

        var text = new StringBuilder("The highest residual risks.\n\n| Risk | Level (current → residual) | Above appetite | Owner | Treatment | Target |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var r in top)
        {
            var appetite = r.AboveAppetite switch { true => "**yes**", false => "no", null => "not recorded" };
            text.Append(CultureInfo.InvariantCulture, $"| {Linked($"{r.RefId} {r.Name}", r.Link)} | {Cell(r.CurrentLevel)} → {Cell(r.ResidualLevel)} | {appetite} | {Cell(r.Owner)} | {Cell(r.Treatment)} | {Date(r.Target)} |\n");
        }

        return text.ToString();
    }

    private static bool NeedsEndorsement(AcceptanceItem item) =>
        item.Expires is not { } expires || item.AcceptedOn is not { } on || expires > on.AddMonths(EndorseAfterMonths);

    private static string AcceptanceSection(PostureSnapshot current, ReportingPeriod quarter)
    {
        var made = current.Acceptances.Where(a => a.AcceptedOn is { } on && quarter.Contains(on)).OrderBy(a => a.AcceptedOn).ToList();
        if (made.Count == 0)
        {
            return None + " No risk was accepted and no exception approved this quarter.";
        }

        var text = new StringBuilder($"Accepted on the board's behalf this quarter. Anything valid for more than {Number(EndorseAfterMonths)} months, or with no expiry, needs the board's endorsement.\n\n| Item | Kind | Accepted by | On | Expires | |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var a in made)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Linked($"{a.RefId} {a.Name}", a.Link)} | {Kind(a.Kind)} | {Cell(a.AcceptedBy)} | {Date(a.AcceptedOn)} | {Date(a.Expires)} | {(NeedsEndorsement(a) ? "**board endorsement**" : "")} |\n");
        }

        return text.ToString();
    }

    private static string AssuranceSection(PostureSnapshot current, ReportingPeriod quarter)
    {
        var text = new StringBuilder();
        foreach (var programme in current.Programmes)
        {
            var group = Last(programme.Groups);
            var coverage = programme.Coverage.Where(c => c.Group == group?.RefId).ToList();
            var (automated, manual, uncovered) = (coverage.Sum(c => c.Automated), coverage.Sum(c => c.Manual), coverage.Sum(c => c.Uncovered));
            var total = automated + manual + uncovered;
            text.Append(CultureInfo.InvariantCulture, $"- Coverage of {programme.Name} at {group?.Name ?? "all groups"}: ")
                .Append(total == 0
                    ? "not measured: no mapping is configured for the audit.\n"
                    : $"{Number(automated)} of {Number(total)} requirements ({Percent(automated, total)}) checked automatically, {Number(manual)} attested by people, {Number(uncovered)} unanswered.\n");
        }

        var fresh = current.Collectors.Count(c => c.Outcome == "Succeeded" && c.LastRun is { } run && DateOnly.FromDateTime(run.UtcDateTime).AddDays(CommitteeReport.StaleAfterDays) >= quarter.End);
        text.Append(CultureInfo.InvariantCulture, $"- Evidence freshness: {Number(fresh)} of {Number(current.Collectors.Count)} collectors succeeded in the last {Number(CommitteeReport.StaleAfterDays)} days of the quarter.\n");
        var reviews = current.Due.Where(d => d.Kind == DueKind.SupplierReview && d.Due <= quarter.End).ToList();
        text.Append(CultureInfo.InvariantCulture, $"- Supplier assurance: {Number(reviews.Count(r => r.Done))} of {Number(reviews.Count)} supplier reviews due by the quarter's end are complete.\n");
        text.Append("- Independent review: hound does not track it; record the last independent review as evidence in CISO Assistant and state it here.\n");
        return text.ToString();
    }

    private static T? Last<T>(IReadOnlyList<T> items)
        where T : class => items.Count > 0 ? items[^1] : null;

    private static string Percent(int part, int total) => (100.0 * part / total).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string AsksSection(IReadOnlyList<ProgrammePosition> positions, IReadOnlyList<AcceptanceItem> endorse)
    {
        var asks = new List<string>();
        if (positions.Count > 0)
        {
            asks.Add("Confirm the compliance position of each programme (section 1).");
        }

        asks.AddRange(endorse.Select(a => $"Endorse {a.RefId} {a.Name}, accepted by {a.AcceptedBy ?? "management"} until {Date(a.Expires)} (section 4)."));
        asks.AddRange(positions.Where(p => p.Position == CompliancePosition.OffTrack).Select(p => $"Ask management for a recovery plan for {p.Programme.Name} by the next quarter."));
        return asks.Count == 0 ? None : string.Concat(asks.Select((a, i) => $"{Number(i + 1)}. {a}\n"));
    }

    private static string Appendix(PostureSnapshot current, ReportingPeriod quarter)
    {
        var text = new StringBuilder("For reference; the committee reports hold the detail and the actions.\n\n");
        if (current.Collectors.Count > 0)
        {
            text.Append("| Collector | Last run (UTC) | Outcome | Problems |\n| --- | --- | --- | --- |\n");
            foreach (var c in current.Collectors)
            {
                text.Append(CultureInfo.InvariantCulture, $"| {Cell(c.Collector)} | {(c.LastRun is { } run ? run.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never")} | {Cell(c.Outcome)} | {Cell(string.Join(" ", c.Problems))} |\n");
            }

            text.Append('\n');
        }

        var overdue = current.Due.Where(d => !d.Done && d.Due < quarter.End).OrderBy(d => d.Due).ToList();
        text.Append(CultureInfo.InvariantCulture, $"Overdue manual evidence and supplier reviews: {Number(overdue.Count)}")
            .Append(overdue.Count == 0 ? ".\n" : $" ({string.Join("; ", overdue.Select(d => $"{d.Name}, {d.Owner ?? "unassigned"}, due {Date(d.Due)}"))}).\n");
        text.Append(CultureInfo.InvariantCulture, $"Open remediation items: {Number(current.Remediation.Count(r => r.Open))}, of which overdue: {Number(current.Remediation.Count(r => r.Open && r.Due is { } d && d < quarter.End))}.\n");
        return text.ToString();
    }
}
