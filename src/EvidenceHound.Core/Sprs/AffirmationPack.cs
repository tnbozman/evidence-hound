using System.Globalization;
using System.Text;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Sprs;

/// <summary>
/// The yearly affirmation pack (s17-06): what the affirming official reads before affirming continuing compliance with CMMC Level 2
/// (32 CFR 170.22). The SPRS score and the open POA&amp;M come from the programme's plan (s17-03, s17-05); evidence freshness and lapsed
/// manual sources from the month's posture snapshot (s12-04). It gathers and decides nothing: the official affirms, and the GRC team
/// records it in the weekly review. Pure.
/// </summary>
public static class AffirmationPack
{
    public static string Markdown(SspDocument plan, SprsMethodology methodology, PostureSnapshot posture, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(methodology);
        ArgumentNullException.ThrowIfNull(posture);
        var score = SprsScoring.Score(methodology, [.. plan.Requirements.Where(r => r.Ref.Value is not null).Select(r => new SprsRow(r.Ref.Value!, Result(r.Result.Value)))]);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# Annual affirmation pack: {plan.Programme}, {Day(today)}\n\n")
            .Append("Read this before you affirm continuing compliance with CMMC Level 2 in SPRS (32 CFR 170.22). hound gathered it from CISO Assistant ")
            .Append(CultureInfo.InvariantCulture, $"(audit {plan.Audit.Value}) and the posture snapshot for {posture.Period}; it decides nothing.\n\n");

        text.Append("## SPRS score\n\n")
            .Append(CultureInfo.InvariantCulture, $"SPRS score: {score.Score} (from {methodology.Start}, {methodology.Name} {methodology.Version}). ")
            .Append(CultureInfo.InvariantCulture, $"{score.Deductions.Count} requirements take points off; `hound sprs score` lists them. A Conditional Level 2 status needs at least 88.\n")
            .Append(score.SspMissing ? "\nNo system security plan is assessed as implemented (3.12.4): an assessment cannot be completed without one.\n" : "")
            .Append('\n');

        Poam(text, Scope.Poam.Of(plan), today);
        Freshness(text, posture);
        Lapsed(text, posture, today);

        return text.Append("## Before you affirm\n\n")
            .Append("- Every open item above has an owner and a date, and none is past it without a reason you accept.\n")
            .Append("- No item CMMC does not allow on a POA&M is open, and every item closes within 180 days of the Conditional status date.\n")
            .Append("- Stale evidence and lapsed sources are explained, or the evidence is collected again first.\n\n")
            .Append("Affirm in SPRS yourself. Record the affirmation in the weekly review (`hound review`): the GRC team completes the yearly ")
            .Append("affirmation task in CISO Assistant with your affirmation as its evidence. An agent never affirms or records it.\n")
            .ToString();
    }

    private static void Poam(StringBuilder text, IReadOnlyList<PoamItem> items, DateOnly today)
    {
        text.Append("## Open POA&M items\n\n");
        if (items.Count == 0)
        {
            text.Append("No open POA&M items.\n\n");
            return;
        }

        text.Append("| Requirement | Status | Control | Owner | ETA | Note |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var i in items)
        {
            var late = DateOnly.TryParseExact(i.Eta.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eta) && eta < today;
            string[] notes = [.. new[] { i.NotAllowed is null ? null : "not allowed on a CMMC POA&M", late ? "past its ETA" : null }.OfType<string>()];
            text.Append(CultureInfo.InvariantCulture, $"| {i.Requirement.Value} | {i.Status.Value} | {i.Control.Value ?? "-"} | {i.Owner.Value ?? "-"} | {i.Eta.Value ?? "no date"} | ")
                .Append(notes.Length == 0 ? "-" : string.Join("; ", notes)).Append(" |\n");
        }

        text.Append('\n');
    }

    private static void Freshness(StringBuilder text, PostureSnapshot posture)
    {
        text.Append("## Evidence freshness\n\n");
        if (posture.Collectors.Count == 0)
        {
            text.Append("No collectors in the snapshot.\n\n");
            return;
        }

        var taken = DateOnly.FromDateTime(posture.TakenAt.UtcDateTime);
        text.Append(CultureInfo.InvariantCulture, $"A collector with no run in the {CommitteeReport.StaleAfterDays} days before the snapshot ({Day(taken)}) is stale.\n\n")
            .Append("| Collector | Last run | Outcome | Freshness |\n| --- | --- | --- | --- |\n");
        foreach (var c in posture.Collectors.OrderBy(c => c.Collector, StringComparer.Ordinal))
        {
            var last = c.LastRun is { } run ? DateOnly.FromDateTime(run.UtcDateTime) : (DateOnly?)null;
            var fresh = last is { } day && day.AddDays(CommitteeReport.StaleAfterDays) >= taken;
            text.Append(CultureInfo.InvariantCulture, $"| {c.Collector} | {(last is { } d ? Day(d) : "never")} | {c.Outcome} | {(fresh ? "fresh" : "stale")} |\n");
        }

        text.Append('\n');
    }

    private static void Lapsed(StringBuilder text, PostureSnapshot posture, DateOnly today)
    {
        text.Append("## Lapsed manual sources\n\n");
        var lapsed = posture.Due.Where(d => d.Kind == DueKind.ManualEvidence && !d.Done && d.Due < today).OrderBy(d => d.Due).ToArray();
        if (lapsed.Length == 0)
        {
            text.Append("No lapsed manual sources.\n\n");
            return;
        }

        text.Append("| Evidence | Owner | Due | Days overdue |\n| --- | --- | --- | --- |\n");
        foreach (var d in lapsed)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {d.Name} | {d.Owner ?? "-"} | {Day(d.Due)} | {today.DayNumber - d.Due.DayNumber} |\n");
        }

        text.Append('\n');
    }

    private static ComplianceResult Result(string? value) =>
        Enum.GetValues<ComplianceResult>().FirstOrDefault(r => Names.Of(r) == value, ComplianceResult.NotAssessed);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
