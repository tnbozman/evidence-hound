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
    public static IReadOnlyList<RequirementResult> OwnedResults(FrameworkMapping mapping, IReadOnlyList<CheckResult> checks)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(checks);

        var reported = checks.Select(c => c.CheckId).ToHashSet();
        var owned = mapping.Requirements.Where(r => r.Checks.All(reported.Contains)).Select(r => r.RequirementRef).ToHashSet(StringComparer.Ordinal);
        return [.. MappingEngine.ApplyMapping(mapping, checks).Where(r => owned.Contains(r.RequirementRef))];
    }
}
