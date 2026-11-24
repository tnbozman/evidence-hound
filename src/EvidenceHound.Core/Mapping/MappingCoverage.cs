using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Mapping;

/// <summary>How many requirements under one parent node and implementation group are answered by collectors, by people, or by nothing.</summary>
/// <param name="ParentUrn">The parent node, such as an Essential Eight strategy.</param>
/// <param name="Group">The implementation group, such as a maturity level.</param>
/// <param name="Automated">Requirements with at least one collector check.</param>
/// <param name="Manual">Requirements answered only by checks a person attests through a manual source, or listed as manual-only.</param>
/// <param name="Uncovered">Requirements nothing answers.</param>
public sealed record CoverageCount(string ParentUrn, string Group, int Automated, int Manual, int Uncovered);

/// <summary>s12-01: mapping coverage of a framework snapshot for an audit's selected implementation groups.</summary>
public static class MappingCoverage
{
    /// <summary>Counts per parent node (in snapshot order) and selected group (in the order given).</summary>
    public static IReadOnlyList<CoverageCount> Count(
        FrameworkSnapshot snapshot,
        FrameworkMapping mapping,
        IEnumerable<RequirementKey> manualOnly,
        IReadOnlySet<CheckId> manualChecks,
        IReadOnlyList<string> groups) => throw new NotImplementedException();
}
