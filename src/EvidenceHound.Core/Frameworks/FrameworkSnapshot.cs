using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Frameworks;

/// <summary>
/// What Evidence Hound needs to know about one framework without asking CISO Assistant: its URNs, version, implementation groups
/// and every assessable requirement node. Generated from a library file with <c>hound framework snapshot</c> and committed.
/// </summary>
public sealed record FrameworkSnapshot(
    string LibraryUrn,
    string FrameworkUrn,
    int Version,
    IReadOnlyList<ImplementationGroup> Groups,
    IReadOnlyList<SnapshotNode> Nodes)
{
    /// <summary>The most characters a node's title keeps.</summary>
    public const int TitleLength = 80;

    /// <summary>The named nodes above the requirements, such as an Essential Eight strategy; empty in snapshots made before s13.</summary>
    public IReadOnlyList<SnapshotParent> Parents { get; init; } = [];

    /// <summary>The libraries CISO Assistant loads with this one, such as a threat catalogue (s15-01); empty in older snapshots.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>A parent node's name for reports: its library name, else the last part of its URN (such as <c>mfa</c>).</summary>
    public string ParentName(string urn)
    {
        ArgumentNullException.ThrowIfNull(urn);
        return Parents.FirstOrDefault(p => p.Urn == urn)?.Name ?? urn[(urn.LastIndexOf(':') + 1)..];
    }

    /// <summary>
    /// The top-level node above a parent node, such as an ASD Essentials domain above a principle: follows each parent's own parent.
    /// A node with no known parent is its own top.
    /// </summary>
    public string TopUrn(string urn)
    {
        ArgumentNullException.ThrowIfNull(urn);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = urn;
        while (seen.Add(current) && Parents.FirstOrDefault(p => p.Urn == current)?.ParentUrn is { } parent)
        {
            current = parent;
        }

        return current;
    }

    /// <summary>The nodes a requirement key names: every node with the ref, or the one node with the URN. Empty when none.</summary>
    public IReadOnlyList<SnapshotNode> Resolve(RequirementKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return [.. Nodes.Where(node => (key.Kind == RequirementKeyKind.Urn ? node.Urn : node.RefId) == key.Value)];
    }
}

/// <summary>A node that groups requirements, such as an Essential Eight strategy or an AI6 practice: URN, ref_id and name.</summary>
public sealed record SnapshotParent(string Urn, string? RefId, string Name)
{
    /// <summary>The node above this one, such as an ASD Essentials domain above a principle; null at the top.</summary>
    public string? ParentUrn { get; init; }
}

/// <summary>One implementation group, such as a maturity level or a priority phase.</summary>
public sealed record ImplementationGroup(string RefId, string Name);

/// <summary>An assessable requirement node: URN, ref_id, parent URN, implementation groups and a short title.</summary>
public sealed record SnapshotNode(string Urn, string? RefId, string? ParentUrn, IReadOnlyList<string> Groups, string Title)
{
    /// <summary>The node's full text on one line; empty in snapshots made before s11-16.</summary>
    public string Description { get; init; } = "";

    /// <summary>The URNs of the reference controls the library links the node to, such as <c>…:doc-pol:tech.mfa</c>.</summary>
    public IReadOnlyList<string> ReferenceControls { get; init; } = [];
}
