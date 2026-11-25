using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Mapping;

/// <summary>How many requirements under one parent node and implementation group are answered by collectors, by people, or by nothing.</summary>
/// <param name="ParentUrn">The parent node, such as an Essential Eight strategy.</param>
/// <param name="Group">The implementation group, such as a maturity level.</param>
/// <param name="Automated">Requirements with at least one collector check.</param>
/// <param name="Manual">Requirements answered only by checks a person attests through a manual source, or listed as manual-only.</param>
/// <param name="Uncovered">Requirements nothing answers.</param>
/// <param name="Draft">s14-03: requirements answered only by draft rows, which runs do not publish until a person reviews them.</param>
public sealed record CoverageCount(string ParentUrn, string Group, int Automated, int Manual, int Uncovered, int Draft = 0);

/// <summary>s12-01: mapping coverage of a framework snapshot for an audit's selected implementation groups.</summary>
public static class MappingCoverage
{
    /// <summary>Counts per parent node (in snapshot order) and selected group (in the order given).</summary>
    public static IReadOnlyList<CoverageCount> Count(
        FrameworkSnapshot snapshot,
        FrameworkMapping mapping,
        IEnumerable<RequirementKey> manualOnly,
        IReadOnlySet<CheckId> manualChecks,
        IReadOnlyList<string> groups) =>
        CountBy(snapshot, mapping, manualOnly, manualChecks, groups, node => node.ParentUrn ?? "");

    /// <summary>
    /// s14-01: counts per top-level node (in snapshot order) and selected group, such as an ASD Essentials domain, which sits above the
    /// principle each action belongs to. For a framework with one level of parents this is the same as <see cref="Count"/>.
    /// </summary>
    public static IReadOnlyList<CoverageCount> CountByTop(
        FrameworkSnapshot snapshot,
        FrameworkMapping mapping,
        IEnumerable<RequirementKey> manualOnly,
        IReadOnlySet<CheckId> manualChecks,
        IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CountBy(snapshot, mapping, manualOnly, manualChecks, groups, node => node.ParentUrn is null ? "" : snapshot.TopUrn(node.ParentUrn));
    }

    private static IReadOnlyList<CoverageCount> CountBy(
        FrameworkSnapshot snapshot,
        FrameworkMapping mapping,
        IEnumerable<RequirementKey> manualOnly,
        IReadOnlySet<CheckId> manualChecks,
        IReadOnlyList<string> groups,
        Func<SnapshotNode, string> parentOf)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(manualOnly);
        ArgumentNullException.ThrowIfNull(manualChecks);
        ArgumentNullException.ThrowIfNull(groups);

        var kinds = Kinds(snapshot, mapping, manualOnly, manualChecks);
        return
        [
            .. snapshot.Nodes.Select(parentOf).Distinct(StringComparer.Ordinal).SelectMany(parent => groups.Select(group =>
                Counted(parent, group, [.. snapshot.Nodes.Where(n => parentOf(n) == parent && n.Groups.Contains(group)).Select(n => kinds.GetValueOrDefault(n.Urn, CoverageKind.Uncovered))])))
            .Where(count => count.Automated + count.Manual + count.Uncovered + count.Draft > 0),
        ];
    }

    private static Dictionary<string, CoverageKind> Kinds(
        FrameworkSnapshot snapshot, FrameworkMapping mapping, IEnumerable<RequirementKey> manualOnly, IReadOnlySet<CheckId> manualChecks)
    {
        var kinds = new Dictionary<string, CoverageKind>(StringComparer.Ordinal);
        var answered = mapping.Requirements
            .Select(r => (r.Key, Kind: r.Draft ? CoverageKind.Draft : r.Checks.All(manualChecks.Contains) ? CoverageKind.Manual : CoverageKind.Automated))
            .Concat(manualOnly.Select(key => (Key: key, Kind: CoverageKind.Manual)));
        foreach (var (key, kind) in answered)
        {
            foreach (var node in snapshot.Resolve(key))
            {
                kinds[node.Urn] = kinds.TryGetValue(node.Urn, out var existing) && existing < kind ? existing : kind;
            }
        }

        return kinds;
    }

    private static CoverageCount Counted(string parent, string group, IReadOnlyList<CoverageKind> kinds) => new(
        parent,
        group,
        kinds.Count(k => k == CoverageKind.Automated),
        kinds.Count(k => k == CoverageKind.Manual),
        kinds.Count(k => k == CoverageKind.Uncovered),
        kinds.Count(k => k == CoverageKind.Draft));

    /// <summary>In order of strength: where two rows answer one requirement, the stronger kind counts.</summary>
    private enum CoverageKind
    {
        Automated,
        Manual,
        Draft,
        Uncovered,
    }
}
