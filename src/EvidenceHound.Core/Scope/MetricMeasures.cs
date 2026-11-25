using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The measures a run computes for a metric instance with <c>source: hound</c> (s16-03), each a percentage, or null when there is nothing to
/// measure. Pure. Each check counts once, by its latest result.
/// </summary>
public static class MetricMeasures
{
    /// <summary>How long a check's evidence stays fresh: a week, as the committee report counts a collector stale.</summary>
    public static TimeSpan FreshFor { get; } = TimeSpan.FromDays(7);

    /// <summary>The share of matching checks that pass. Not applicable results are left out; partial and error do not pass.</summary>
    public static double? CheckPassRate(IReadOnlyList<string> patterns, IReadOnlyList<CheckResult> latest)
    {
        var counted = Latest(patterns, latest).Where(r => r.Outcome != CheckOutcome.NotApplicable).ToList();
        return Percent(counted.Count(r => r.Outcome == CheckOutcome.Pass), counted.Count);
    }

    /// <summary>The share of an audit's requirements that are compliant. Not applicable rows are left out; not assessed rows count against it.</summary>
    public static double? RequirementsCompliantPct(IReadOnlyList<ComplianceResult> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var counted = rows.Where(r => r != ComplianceResult.NotApplicable).ToList();
        return Percent(counted.Count(r => r == ComplianceResult.Compliant), counted.Count);
    }

    /// <summary>The share of matching checks (every check when there are no patterns) whose latest result is at most <see cref="FreshFor"/> old.</summary>
    public static double? EvidenceFreshnessPct(IReadOnlyList<string> patterns, IReadOnlyList<CheckResult> latest, DateTimeOffset now)
    {
        var counted = Latest(patterns, latest).ToList();
        return Percent(counted.Count(r => now - r.CollectedAt <= FreshFor), counted.Count);
    }

    /// <summary>The share of the collectors a run ran that succeeded.</summary>
    public static double? CollectorSuccessRate(int succeeded, int ran) => Percent(succeeded, ran);

    /// <summary>A check id matches itself, and <c>a.b.*</c> matches every check below <c>a.b</c>.</summary>
    public static bool Matches(string pattern, string checkId)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(checkId);
        return pattern.EndsWith(".*", StringComparison.Ordinal)
            ? checkId.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, checkId, StringComparison.Ordinal);
    }

    private static IEnumerable<CheckResult> Latest(IReadOnlyList<string> patterns, IReadOnlyList<CheckResult> latest)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(latest);
        return latest
            .Where(r => patterns.Count == 0 || patterns.Any(p => Matches(p, r.CheckId.Value)))
            .GroupBy(r => r.CheckId.Value, StringComparer.Ordinal)
            .Select(g => g.MaxBy(r => r.CollectedAt)!);
    }

    private static double? Percent(int part, int whole) => whole == 0 ? null : 100.0 * part / whole;
}
