using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Reporting;

/// <summary>s12-04: builds a programme's posture from its framework snapshot and the audit's rows, and counts results per group and parent.</summary>
public static class Posture
{
    /// <summary>The programme's posture: only rows in its selected groups, each with its parent from the framework snapshot.</summary>
    public static ProgrammePosture Programme(
        string id, string name, FrameworkSnapshot framework, string audit, IReadOnlyList<string> groups, IReadOnlyList<CurrentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(rows);
        var parentOf = framework.Nodes.GroupBy(n => n.Urn, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().ParentUrn, StringComparer.Ordinal);
        var requirements = rows
            .Where(r => r.Groups.Any(groups.Contains))
            .Select(r => new RequirementPosture(r.Urn, r.RefId, parentOf.GetValueOrDefault(r.Urn), r.Groups, r.Result))
            .ToList();
        var parents = framework.Nodes
            .Select(n => n.ParentUrn)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(urn => new ParentNode(urn, ShortName(urn)))
            .ToList();
        return new ProgrammePosture(
            id,
            name,
            framework.FrameworkUrn,
            framework.Version,
            audit,
            [.. groups.Select(g => framework.Groups.FirstOrDefault(fg => fg.RefId == g) ?? new ImplementationGroup(g, g))],
            parents,
            requirements,
            Count(requirements, groups, parents));
    }

    /// <summary>Per selected group: one count over the group, then one per parent node in <paramref name="parents"/> order.</summary>
    public static IReadOnlyList<ResultCount> Count(IReadOnlyList<RequirementPosture> rows, IReadOnlyList<string> groups, IReadOnlyList<ParentNode> parents)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(parents);
        return
        [
            .. groups.SelectMany(group =>
            {
                var inGroup = rows.Where(r => r.Groups.Contains(group)).ToList();
                return new[] { Counted(group, null, inGroup) }
                    .Concat(parents.Select(p => Counted(group, p.Urn, [.. inGroup.Where(r => r.ParentUrn == p.Urn)])).Where(c => c.Total > 0));
            }),
        ];
    }

    /// <summary>The last part of a node URN, such as <c>mfa</c>.</summary>
    public static string ShortName(string urn)
    {
        ArgumentNullException.ThrowIfNull(urn);
        return urn[(urn.LastIndexOf(':') + 1)..];
    }

    private static ResultCount Counted(string group, string? parent, IReadOnlyList<RequirementPosture> rows) => new(
        group,
        parent,
        rows.Count(r => r.Result == ComplianceResult.Compliant),
        rows.Count(r => r.Result == ComplianceResult.PartiallyCompliant),
        rows.Count(r => r.Result == ComplianceResult.NonCompliant),
        rows.Count(r => r.Result == ComplianceResult.NotAssessed),
        rows.Count(r => r.Result == ComplianceResult.NotApplicable));
}
