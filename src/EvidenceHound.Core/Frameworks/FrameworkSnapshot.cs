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

    /// <summary>The nodes a requirement key names: every node with the ref, or the one node with the URN. Empty when none.</summary>
    public IReadOnlyList<SnapshotNode> Resolve(RequirementKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return [.. Nodes.Where(node => (key.Kind == RequirementKeyKind.Urn ? node.Urn : node.RefId) == key.Value)];
    }
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
