using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Onboarding.Capabilities;

/// <summary>What read-only lookups found in the environment: subscribed licences and the kinds of Intune configuration present, and where from.</summary>
public sealed record EnvironmentFacts(string Source, IReadOnlyList<string> Licences, IReadOnlyList<string> ConfigurationTypes)
{
    public static EnvironmentFacts None { get; } = new("", [], []);
}

/// <summary>A pre-filled answer for one mitigation: what the signals suggest, if anything, and each signal with its source. Never a decision.</summary>
public sealed record CapabilityProposal(string Mitigation, CapabilityProvision? Suggested, IReadOnlyList<string> Systems, IReadOnlyList<string> Signals);

/// <summary>The person's answer to a proposal.</summary>
public sealed record CapabilityAnswer(string Kind, IReadOnlyList<string> Systems)
{
    public static CapabilityAnswer Accept { get; } = new("accept", []);

    public static CapabilityAnswer Manual { get; } = new("manual", []);

    public static CapabilityAnswer Gap { get; } = new("gap", []);

    public static CapabilityAnswer System(IReadOnlyList<string> systems) => new("system", systems);
}

public static class CapabilitySignals
{
    public static IReadOnlyList<CapabilityProposal> Prefill(IReadOnlyList<Mitigation> mitigations, IReadOnlyList<ScopeSystem> systems, EnvironmentFacts facts, string? microsoftSystem) => [];
}

public static class CapabilityAnswers
{
    public static Result<ScopeCapability> Decide(CapabilityProposal proposal, CapabilityAnswer answer, string acceptedBy, string approvedBy, DateOnly today) =>
        Result.Fail<ScopeCapability>(new Problem("capability.todo", "not yet"));
}
