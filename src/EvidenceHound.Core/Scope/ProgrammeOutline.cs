using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>A programme's shape, from its framework snapshot alone: the assessable requirements in each selected group, by parent node.</summary>
public sealed record ProgrammeOutline(ScopeProgramme Programme, int Version, IReadOnlyList<GroupOutline> Groups)
{
    private static readonly string TopLevel = "(top level)";

    /// <summary>Counts each assessable node once in every selected group it belongs to, under its parent; parents in snapshot order.</summary>
    public static ProgrammeOutline Of(ScopeProgramme programme, FrameworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(snapshot);
        var parents = snapshot.Nodes.Select(n => n.ParentUrn).Distinct().ToArray();
        return new(programme, snapshot.Version, [.. programme.Groups.Select(g => Group(g, snapshot, parents))]);
    }

    private static GroupOutline Group(string group, FrameworkSnapshot snapshot, IReadOnlyList<string?> parents)
    {
        var nodes = snapshot.Nodes.Where(n => n.Groups.Contains(group, StringComparer.Ordinal)).ToArray();
        return new(
            group,
            snapshot.Groups.FirstOrDefault(g => g.RefId == group)?.Name ?? group,
            nodes.Length,
            [.. parents.Select(p => new ParentOutline(p, Name(p), nodes.Count(n => n.ParentUrn == p))).Where(p => p.Requirements > 0)]);
    }

    /// <summary>A parent's short name: the last part of its URN, such as <c>mfa</c> for the Essential Eight's multi-factor authentication.</summary>
    private static string Name(string? parentUrn) => parentUrn is null ? TopLevel : parentUrn[(parentUrn.LastIndexOf(':') + 1)..];
}

/// <summary>One implementation group of a programme: its assessable requirement count, and the same per parent node.</summary>
public sealed record GroupOutline(string RefId, string Name, int Requirements, IReadOnlyList<ParentOutline> Parents);

/// <summary>A parent node (an Essential Eight strategy, an AI6 practice) and how many of its requirements are in the group. A null URN is the top level.</summary>
public sealed record ParentOutline(string? Urn, string Name, int Requirements);
