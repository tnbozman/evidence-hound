namespace EvidenceHound.Core.Scope;

/// <summary>What the scope manages in CISO Assistant, in dependency order: a plan creates them in this order and deletes them in reverse.</summary>
public enum ScopeKind
{
    Framework,
    Domain,
    Perimeter,
    System,
    User,
    Team,
    RoleAssignment,
    Audit,
    Document,
    Control,
    Assessment,
}

/// <summary>
/// One object as the scope declares it or CISO Assistant holds it: its kind, its stable key, and the fields <c>hound</c> keeps in step,
/// as text (left out when empty). <see cref="Initial"/> fields are written only when the object is created, because CISO Assistant
/// or a collector owns them afterwards, such as an applied control's status.
/// </summary>
public sealed record ScopeObject(ScopeKind Kind, string Key, IReadOnlyDictionary<string, string> Fields)
{
    public IReadOnlyDictionary<string, string> Initial { get; init; } = new Dictionary<string, string>();

    /// <summary>A document's files, with their SHA-256, for the writer to upload; the plan compares their hashes (the <c>hashes</c> field).</summary>
    public IReadOnlyList<ScopeFile> Files { get; init; } = [];
}

/// <summary>A file of a document: its path and the SHA-256 of its content, as lowercase hex.</summary>
public sealed record ScopeFile(string Path, string Sha256);

/// <summary>
/// An object CISO Assistant holds, with the state fingerprint <c>hound</c> last wrote on it (from the marker at the end of its description),
/// or null when <c>hound</c> has never written it. An object with a marker is managed by <c>hound</c>.
/// </summary>
public sealed record ActualObject(ScopeObject Held, string? AppliedState)
{
    public bool Managed => AppliedState is not null;

    /// <summary>An audit locked in CISO Assistant: <c>hound</c> never changes it or its rows.</summary>
    public bool Locked { get; init; }
}

public enum ScopeAction
{
    Create,
    Update,
    Delete,
}

/// <summary>One field of a change: its value in CISO Assistant (null when unset) and the value the scope declares (null to clear).</summary>
public sealed record FieldChange(string Field, string? Old, string? New);

/// <summary>
/// One write the plan makes. <paramref name="State"/> is the fingerprint to record on the object, marking it managed; null for objects that carry
/// no marker (frameworks and assessments) and for deletes. An update with no fields only marks an object the scope declares as managed.
/// </summary>
public sealed record ScopeChange(ScopeKind Kind, string Key, ScopeAction Action, IReadOnlyList<FieldChange> Fields, string? State)
{
    /// <summary>The object to write: the declared one, with the links CISO Assistant already holds added unless the plan prunes. Null for a delete.</summary>
    public ScopeObject? Desired { get; init; }
}

public enum DriftReason
{
    /// <summary>A managed object was changed in CISO Assistant since <c>hound</c> last wrote it.</summary>
    Edited,

    /// <summary>An object in CISO Assistant that the scope does not declare.</summary>
    NotInScope,

    /// <summary>A change to a locked audit, or to its rows: reported, never written.</summary>
    Locked,

    /// <summary>A framework loaded from an older library version than the scope's snapshot: reported, never upgraded silently.</summary>
    Outdated,
}

/// <summary>An object in CISO Assistant that differs from what <c>hound</c> last wrote, or that the scope does not declare.</summary>
public sealed record ScopeDrift(ScopeKind Kind, string Key, DriftReason Reason, bool Managed, IReadOnlyList<FieldChange> Fields);

/// <summary>The writes that bring CISO Assistant in line with the scope, in dependency order, and the drift found on the way.</summary>
public sealed record ScopePlan(IReadOnlyList<ScopeChange> Changes, IReadOnlyList<ScopeDrift> Drift)
{
    public bool IsEmpty => Changes.Count == 0 && Drift.Count == 0;
}
