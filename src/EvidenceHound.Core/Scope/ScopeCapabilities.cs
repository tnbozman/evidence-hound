namespace EvidenceHound.Core.Scope;

/// <summary>What provides a mitigation: systems in scope, manual evidence, or nothing yet (a gap).</summary>
public enum CapabilityProvision
{
    System,
    Manual,
    Gap,
}

/// <summary>
/// <c>scope/capabilities.yaml</c>: for one framework, which product in the environment provides each mitigation, decided before systems are
/// onboarded. It only decides which system answers each requirement; results stay in each system's own audit.
/// </summary>
public sealed record ScopeCapabilityMap(ScopeRef Framework, IReadOnlyList<ScopeCapability> Mitigations, SourceLine At);

/// <summary>One mitigation's decision: what provides it, who owns and approved it, and how its evidence arrives.</summary>
public sealed record ScopeCapability(string Mitigation, CapabilityProvision ProvidedBy, SourceLine At)
{
    public string? Name { get; init; }

    /// <summary>The systems in <c>scope/setup.yaml</c> that provide it, when <see cref="ProvidedBy"/> is <see cref="CapabilityProvision.System"/>.</summary>
    public IReadOnlyList<ScopeRef> Systems { get; init; } = [];

    /// <summary>The third party that supplies it, by <c>ref_id</c>.</summary>
    public ScopeRef? Supplier { get; init; }

    public string? Owner { get; init; }

    /// <summary><c>collector</c>, <c>profile</c> or <c>manual</c>.</summary>
    public string? Evidence { get; init; }

    /// <summary>The collector or profile name the evidence comes through.</summary>
    public string? Route { get; init; }

    public string? ApprovedBy { get; init; }

    public string? ApprovedOn { get; init; }

    public string? Reason { get; init; }

    /// <summary>The signals that pre-filled the answer, each naming where it came from; a record, never the decision.</summary>
    public IReadOnlyList<string> Signals { get; init; } = [];
}
