namespace EvidenceHound.Core.Scope;

/// <summary>
/// A system security plan declared in <c>scope/ssp.yaml</c> (s11-02): the programme it covers, the profile it is laid out by, the system's
/// name, its boundary (a perimeter), its version, its roles, its documents (by document id from an audit) and the hand-written sections.
/// </summary>
public sealed record ScopeSsp(
    ScopeRef Programme,
    string Profile,
    string SystemName,
    ScopeRef Perimeter,
    string Version,
    IReadOnlyList<SspRole> Roles,
    IReadOnlyList<ScopeRef> Documents,
    IReadOnlyList<SspSection> Sections,
    SourceLine At)
{
    /// <summary>The system's classification, such as OFFICIAL or PROTECTED.</summary>
    public string? Classification { get; init; }

    /// <summary>operational, under development, or undergoing a major modification.</summary>
    public string? OperationalStatus { get; init; }

    /// <summary>major application or general support system.</summary>
    public string? SystemType { get; init; }

    /// <summary>The signed versions (s11-05), recorded by pull request like an audit period's sign-off.</summary>
    public IReadOnlyList<SspSignOff> SignOffs { get; init; } = [];
}

/// <summary>
/// One signed version of the plan: the version, who approved it and when, the SHA-256 of the reviewed draft, and the SHA-256 of the snapshot
/// it was built from (optional).
/// </summary>
public sealed record SspSignOff(string Version, string By, string Date, string Sha256, string? Snapshot)
{
    public SourceLine? At { get; init; }
}

/// <summary>A role in the plan, such as the authorising official, held by a user (by email) or a team from <c>access.yaml</c>.</summary>
public sealed record SspRole(string Role, ScopeRef Holder);

/// <summary>A part of the plan that cannot be generated, such as the environment description: a Markdown file in the repo.</summary>
public sealed record SspSection(string Name, string File, SourceLine At);
