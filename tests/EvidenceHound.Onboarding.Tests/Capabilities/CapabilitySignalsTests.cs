using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Capabilities;

namespace EvidenceHound.Onboarding.Tests.Capabilities;

/// <summary>s09-28: read-only signals and the systems in scope pre-fill each mitigation's answer with where it came from; the person decides.</summary>
public sealed class CapabilitySignalsTests
{
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    private static readonly Mitigation[] Mitigations =
    [
        new("ac", [Node("ac", "ISM-0843")]),
        new("mfa", [Node("mfa", "ISM-1504")]),
        new("rb", [Node("rb", "ISM-1511")]),
    ];

    private static readonly ScopeSystem[] Systems =
    [
        new("Example 365", null, SystemType.Primary, null, null, [], new Dictionary<string, string>(), At),
        new("Example backup", null, SystemType.Primary, null, null, [], new Dictionary<string, string> { ["capabilities"] = "rb" }, At),
    ];

    private static readonly EnvironmentFacts Facts = new(
        "Microsoft Graph (recorded 2026-11-23)",
        ["AAD_PREMIUM_P2", "INTUNE_A"],
        ["#microsoft.graph.windows10EndpointProtectionConfiguration"]);

    [Fact]
    public void Signals_prefill_but_never_decide()
    {
        var proposals = CapabilitySignals.Prefill(Mitigations, Systems, Facts, microsoftSystem: "Example 365");

        var mfa = proposals.Single(p => p.Mitigation == "mfa");
        mfa.Suggested.ShouldBe(CapabilityProvision.System);
        mfa.Systems.ShouldBe(["Example 365"]);
        mfa.Signals.ShouldBe(["Microsoft Graph (recorded 2026-11-23): licence AAD_PREMIUM_P2 (Microsoft Entra ID P2) is subscribed"]);
        var ac = proposals.Single(p => p.Mitigation == "ac");
        ac.Signals.ShouldBe(["Microsoft Graph (recorded 2026-11-23): Intune holds an endpoint protection configuration, which can carry application control"]);
        var rb = proposals.Single(p => p.Mitigation == "rb");
        rb.Suggested.ShouldBe(CapabilityProvision.System);
        rb.Systems.ShouldBe(["Example backup"]);
        rb.Signals.ShouldBe(["scope/setup.yaml: Example backup is tagged capabilities: rb"]);

        // A proposal is never a decision: it takes the person's answer and an approver to become one, and keeps its signals as a record.
        CapabilityAnswers.Decide(mfa, CapabilityAnswer.Accept, "", "GRC lead", new DateOnly(2026, 11, 23)).ShouldBeOfType<Core.Results.Result<ScopeCapability>.Fail>();
        var decided = CapabilityAnswers.Decide(mfa, CapabilityAnswer.Accept, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value();
        (decided.ProvidedBy, decided.ApprovedBy, decided.ApprovedOn, decided.Signals.Count).ShouldBe((CapabilityProvision.System, "GRC lead", "2026-11-23", 1));
        decided.Reason.ShouldBe("Accepted by Alex Example from the signals.");
        CapabilityAnswers.Decide(ac, CapabilityAnswer.System(["Example app control"]), "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value()
            .Systems.Select(s => s.Value).ShouldBe(["Example app control"]);
        CapabilityAnswers.Decide(ac, CapabilityAnswer.Gap, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value().ProvidedBy.ShouldBe(CapabilityProvision.Gap);
    }

    [Fact]
    public void Without_signals_nothing_is_suggested()
    {
        var proposal = CapabilitySignals.Prefill(Mitigations, [], EnvironmentFacts.None, microsoftSystem: null).Single(p => p.Mitigation == "ac");

        (proposal.Suggested, proposal.Systems.Count, proposal.Signals.Count).ShouldBe(((CapabilityProvision?)null, 0, 0));
        CapabilityAnswers.Decide(proposal, CapabilityAnswer.Accept, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23))
            .ShouldBeOfType<Core.Results.Result<ScopeCapability>.Fail>().Errors.ShouldHaveSingleItem().Code.ShouldBe("capability.nothing_to_accept");
    }

    private static SnapshotNode Node(string mitigation, string refId) =>
        new($"urn:example:{mitigation}:{refId}", refId, $"urn:example:{mitigation}", ["1", "2"], refId);
}
