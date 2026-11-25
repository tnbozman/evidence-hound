using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Runtime.Pipeline;

/// <summary>Which requirement results one collector's run may publish. Pure.</summary>
public static class Ownership
{
    /// <summary>
    /// The mapping applied to the checks, keeping only requirements whose every check is among them. A requirement this run cannot
    /// fully assess is left alone, so one collector never overwrites another's result with "not assessed".
    /// </summary>
    public static IReadOnlyList<RequirementResult> OwnedResults(FrameworkMapping mapping, IReadOnlyList<CheckResult> checks) =>
        OwnedResults(mapping, checks, []);

    /// <summary>
    /// s14-05: as <see cref="OwnedResults(FrameworkMapping, IReadOnlyList{CheckResult})"/>, but a requirement may also take checks from
    /// <paramref name="others"/>, the latest saved results of the other collectors, as long as at least one of its checks is this run's.
    /// Each check counts its newest result.
    /// </summary>
    public static IReadOnlyList<RequirementResult> OwnedResults(FrameworkMapping mapping, IReadOnlyList<CheckResult> checks, IReadOnlyList<CheckResult> others)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(others);

        var mine = checks.Select(c => c.CheckId).ToHashSet();
        var reported = mine.Concat(others.Select(c => c.CheckId)).ToHashSet();
        var owned = mapping.Requirements
            .Where(r => r.Checks.All(reported.Contains) && r.Checks.Any(mine.Contains))
            .Select(r => r.RequirementRef)
            .ToHashSet(StringComparer.Ordinal);
        return [.. MappingEngine.ApplyMapping(mapping, [.. checks, .. others]).Where(r => owned.Contains(r.RequirementRef))];
    }
}
