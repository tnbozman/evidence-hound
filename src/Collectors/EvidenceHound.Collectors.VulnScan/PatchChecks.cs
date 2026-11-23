using System.Globalization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>A finding past its patch window: how long it has been open, counted from what, and the window it had.</summary>
public sealed record OverdueFinding(ScanFinding Finding, string CountedFrom, DateTimeOffset Since, string? SinceFile, double AgeHours, int WindowHours)
{
    public int AgeDays => (int)Math.Floor(AgeHours / 24);
}

/// <summary>What the current scan shows against the policy: every overdue finding and every unsupported one.</summary>
public sealed record PatchAssessment(ScanRun Scan, IReadOnlyList<OverdueFinding> Overdue, IReadOnlyList<ScanFinding> Unsupported);

/// <summary>
/// Pure: the three patching checks of a scope, judged from the current scan. A finding's window counts from when its patch was
/// published, else when it was first seen, else the earliest scan in the folder's history that shows it, up to the scan date.
/// </summary>
public static class PatchChecks
{
    public const string AssessmentFile = "patch-assessment.json";

    private static readonly int Named = 20;

    /// <summary>
    /// The scope's checks. Other applications have no unsupported check: removing unsupported applications other than the common
    /// ones is a maturity level 3 requirement (ISM-0304), and Evidence Hound maps ML1 and ML2.
    /// </summary>
    public static IReadOnlyList<string> Ids(string scope) => scope == "other_apps"
        ? [$"vuln.{scope}.scanned_in_interval", $"vuln.{scope}.patched_in_window"]
        : [$"vuln.{scope}.scanned_in_interval", $"vuln.{scope}.patched_in_window", $"vuln.{scope}.unsupported_removed"];

    public static IReadOnlyList<string> All { get; } = [.. VulnScanSource.Scopes.SelectMany(Ids)];

    public static PatchAssessment Assessment(VulnScanSource source, ScanFolder folder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(folder);
        var scan = folder.Current ?? throw new ArgumentException("The folder has no current scan.", nameof(folder));
        var overdue = scan.Findings.Select(f => Age(source.Policy, scan, folder.History, f)).Where(o => o.AgeHours > o.WindowHours).ToArray();
        return new PatchAssessment(scan, overdue, [.. scan.Findings.Where(f => f.Unsupported)]);
    }

    public static Result<IReadOnlyList<CheckResult>> Assess(VulnScanSource source, ScanFolder folder, DateTimeOffset now, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(folder);
        var ids = Ids(source.Scope);
        if (folder.Current is not { } scan)
        {
            var unable = "Unable to assess: no scan export in the drop folder yet.";
            return Results(
                ids,
                now,
                name,
                (CheckOutcome.Fail, $"No scan export in the drop folder yet; {source.Scope} must be scanned at least every {Days(source.Policy.ScanIntervalDays)}. Owner: {source.Owner}.", []),
                (CheckOutcome.Error, unable, []),
                (CheckOutcome.Error, unable, []));
        }

        var assessment = Assessment(source, folder);
        string[] scanned = [VulnScanCollector.ExportName(source, scan.FileName), VulnScanCollector.ScanFile];
        string[] judged = folder.History.Count > 0 ? [.. scanned, PatchChecks.AssessmentFile, VulnScanCollector.HistoryFile] : [.. scanned, PatchChecks.AssessmentFile];
        return Results(ids, now, name, Interval(source, scan, now, scanned), Patched(source.Policy, assessment, judged), Unsupported(assessment, [.. scanned, AssessmentFile]));
    }

    private static (CheckOutcome, string, string[]) Interval(VulnScanSource source, ScanRun scan, DateTimeOffset now, string[] refs)
    {
        var interval = source.Policy.ScanIntervalDays;
        var age = (now.UtcDateTime.Date - scan.ScanDate.UtcDateTime.Date).Days;
        return age <= interval
            ? (CheckOutcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Scanned {Day(scan.ScanDate)} ({scan.FileName}): within the {interval}-day interval for {source.Scope}."), refs)
            : (CheckOutcome.Fail, $"Last scan {Day(scan.ScanDate)} ({scan.FileName}) is {Days(age)} old; {source.Scope} must be scanned at least every {Days(interval)}. Owner: {source.Owner}.", refs);
    }

    private static (CheckOutcome, string, string[]) Patched(PatchPolicy policy, PatchAssessment assessment, string[] refs)
    {
        var scan = assessment.Scan;
        var window = policy.ExploitedWindowHours == policy.PatchWindowDays * 24
            ? Days(policy.PatchWindowDays)
            : string.Create(CultureInfo.InvariantCulture, $"{Days(policy.PatchWindowDays)}, or {policy.ExploitedWindowHours} hours with an exploit");
        var hosts = scan.Findings.Select(f => f.Host).Distinct(StringComparer.Ordinal).Count();
        if (assessment.Overdue.Count == 0)
        {
            return (CheckOutcome.Pass, $"No open finding in the {Day(scan.ScanDate)} scan ({scan.FileName}) is past its window ({window}): {Count(scan.Findings.Count, "finding")} on {Count(hosts, "host")}.", refs);
        }

        var named = Names(assessment.Overdue.Select(Describe), assessment.Overdue.Count);
        return (CheckOutcome.Fail, Shorten(string.Create(CultureInfo.InvariantCulture, $"{assessment.Overdue.Count} of {Count(scan.Findings.Count, "open finding")} in the {Day(scan.ScanDate)} scan ({scan.FileName}) are past their window ({window}): {named}.")), refs);
    }

    private static (CheckOutcome, string, string[]) Unsupported(PatchAssessment assessment, string[] refs)
    {
        var scan = assessment.Scan;
        if (assessment.Unsupported.Count == 0)
        {
            return (CheckOutcome.Pass, $"Nothing unsupported in the {Day(scan.ScanDate)} scan ({scan.FileName}) of {Count(scan.Hosts.Count, "host")}.", refs);
        }

        var hosts = string.Join(", ", assessment.Unsupported.Select(f => f.Host).Distinct(StringComparer.Ordinal).Take(Named));
        var named = Names(assessment.Unsupported.Select(f => $"{f.Host} {f.FindingId} ({f.Name})"), assessment.Unsupported.Count);
        return (CheckOutcome.Fail, Shorten(string.Create(CultureInfo.InvariantCulture, $"{assessment.Unsupported.Count} unsupported in the {Day(scan.ScanDate)} scan ({scan.FileName}), on {hosts}: {named}.")), refs);
    }

    private static OverdueFinding Age(PatchPolicy policy, ScanRun scan, IReadOnlyList<ScanRun> history, ScanFinding finding)
    {
        var window = finding.ExploitAvailable ? policy.ExploitedWindowHours : policy.PatchWindowDays * 24;
        var (from, since, file) = finding switch
        {
            { PatchPublished: { } published } => ("patch_published", published, (string?)null),
            { FirstSeen: { } seen } => ("first_seen", seen, null),
            _ => history.Where(r => r.Findings.Any(f => f.Host == finding.Host && f.FindingId == finding.FindingId)).MinBy(r => r.ScanDate) is { } earliest && earliest.ScanDate < scan.ScanDate
                ? ("first_scanned", earliest.ScanDate, earliest.FileName)
                : ("first_scanned", scan.ScanDate, scan.FileName),
        };
        return new OverdueFinding(finding, from, since, file, (scan.ScanDate - since).TotalHours, window);
    }

    private static string Describe(OverdueFinding o)
    {
        var since = o.CountedFrom switch
        {
            "patch_published" => $"{Days(o.AgeDays)} since the patch was published",
            "first_seen" => $"{Days(o.AgeDays)} since first seen",
            _ => $"{Days(o.AgeDays)} since first scanned ({o.SinceFile})",
        };
        return $"{o.Finding.Host} {o.Finding.FindingId} ({o.Finding.Name}{(o.Finding.ExploitAvailable ? ", exploit available" : "")}, {since})";
    }

    private static string Names(IEnumerable<string> items, int count)
    {
        var named = string.Join(", ", items.Take(Named));
        return count > Named ? string.Create(CultureInfo.InvariantCulture, $"{named}, and {count - Named} more (all in {AssessmentFile})") : named;
    }

    private static Result<IReadOnlyList<CheckResult>> Results(IReadOnlyList<string> ids, DateTimeOffset now, string name, params (CheckOutcome Outcome, string Observation, string[] Refs)[] results) =>
        Result.Combine(ids.Zip(results, (id, r) => CheckId.Create(id).Bind(checkId => CheckResult.Create(checkId, r.Outcome, r.Observation, r.Refs, now, name))));

    private static string Days(int days) => days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : string.Create(CultureInfo.InvariantCulture, $"{count} {noun}s");

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shorten(string observation) =>
        observation.Length <= CheckResult.MaxObservationLength ? observation : string.Concat(observation.AsSpan(0, CheckResult.MaxObservationLength - 1), "…");
}
