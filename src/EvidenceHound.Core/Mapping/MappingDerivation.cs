using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Mapping;

/// <summary>One link of a requirement mapping library: a source requirement row relates to a target requirement row.</summary>
public sealed record RequirementLink(string SourceUrn, string TargetUrn, CrosswalkRelationship Relationship);

/// <summary>A requirement mapping library, such as CISO Assistant's Essential Eight 2023 to ASD Essentials transition mapping.</summary>
public sealed record MappingLibrary(string SourceFrameworkUrn, string TargetFrameworkUrn, IReadOnlyList<RequirementLink> Links);

/// <summary>A row of a derived mapping: the target row, its top-level node's short name, the source refs linked to it and their checks.</summary>
/// <param name="Urn">The target requirement row.</param>
/// <param name="RefId">The target requirement's ref_id, such as <c>IT.PRO-13.4</c>.</param>
/// <param name="Domain">The last part of the top-level node's URN, such as <c>identity-services</c>.</param>
/// <param name="SourceRefs">The source requirements linked to the row, by ref_id.</param>
/// <param name="Checks">The union of the source requirements' checks, in link order; empty when none of them has any.</param>
public sealed record DerivedRequirement(string Urn, string? RefId, string Domain, IReadOnlyList<string> SourceRefs, IReadOnlyList<CheckId> Checks);

/// <summary>A mapping drafted for a new framework, one entry per target row in the selected groups, in snapshot order.</summary>
public sealed record DerivedMapping(string FrameworkUrn, IReadOnlyList<DerivedRequirement> Requirements)
{
    /// <summary>The rows that have checks, by URN, each <c>all_pass</c> and marked draft.</summary>
    public Result<FrameworkMapping> ToMapping() =>
        Result.Combine(Requirements
                .Where(r => r.Checks.Count > 0)
                .Select(r => RequirementMapping.Create(RequirementKey.Urn(r.Urn), r.Checks, MappingRule.AllPass).Map(m => m with { Draft = true })))
            .Bind(rows => FrameworkMapping.Create(FrameworkUrn, rows));
}

/// <summary>
/// s14-01: drafts a mapping for a new framework from an existing framework's mapping and a transition mapping library. Each target row
/// gets the union of the checks of the source rows linked to it. Pure; the draft needs a person's review before it is published.
/// </summary>
public static class MappingDerivation
{
    public static Result<DerivedMapping> Derive(
        FrameworkMapping source, FrameworkSnapshot sourceSnapshot, FrameworkSnapshot target, MappingLibrary library, IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(groups);

        if (library.SourceFrameworkUrn != source.FrameworkUrn || library.SourceFrameworkUrn != sourceSnapshot.FrameworkUrn || library.TargetFrameworkUrn != target.FrameworkUrn)
        {
            return Result.Fail<DerivedMapping>(new Problem(
                "derive.frameworks_mismatch",
                $"The mapping library links {library.SourceFrameworkUrn} to {library.TargetFrameworkUrn}, but the mapping is for {source.FrameworkUrn}, "
                + $"the source snapshot for {sourceSnapshot.FrameworkUrn} and the target snapshot for {target.FrameworkUrn}."));
        }

        var checks = ChecksBySourceUrn(source, sourceSnapshot);
        var refs = sourceSnapshot.Nodes.GroupBy(n => n.Urn, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().RefId ?? g.Key, StringComparer.Ordinal);
        var links = library.Links.ToLookup(l => l.TargetUrn, StringComparer.Ordinal);
        return Result.Ok(new DerivedMapping(
            target.FrameworkUrn,
            [
                .. target.Nodes
                    .Where(n => groups.Count == 0 || n.Groups.Any(groups.Contains))
                    .Select(n =>
                    {
                        var sources = links[n.Urn].Select(l => l.SourceUrn).Distinct(StringComparer.Ordinal).ToList();
                        return new DerivedRequirement(
                            n.Urn,
                            n.RefId,
                            Short(n.ParentUrn is null ? n.Urn : target.TopUrn(n.ParentUrn)),
                            [.. sources.Select(urn => refs.GetValueOrDefault(urn, urn)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                            [.. sources.SelectMany(urn => checks.GetValueOrDefault(urn) ?? []).Distinct()]);
                    }),
            ]));
    }

    private static Dictionary<string, List<CheckId>> ChecksBySourceUrn(FrameworkMapping source, FrameworkSnapshot snapshot)
    {
        var checks = new Dictionary<string, List<CheckId>>(StringComparer.Ordinal);
        foreach (var requirement in source.Requirements.Where(r => !r.Draft))
        {
            foreach (var node in snapshot.Resolve(requirement.Key))
            {
                var list = checks.TryGetValue(node.Urn, out var existing) ? existing : checks[node.Urn] = [];
                list.AddRange(requirement.Checks.Where(c => !list.Contains(c)));
            }
        }

        return checks;
    }

    private static string Short(string urn) => urn[(urn.LastIndexOf(':') + 1)..];
}
