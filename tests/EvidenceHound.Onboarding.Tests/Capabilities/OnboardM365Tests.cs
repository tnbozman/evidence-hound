using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Capabilities;

namespace EvidenceHound.Onboarding.Tests.Capabilities;

/// <summary>
/// s09-29: the Microsoft 365 path of environment onboarding. Licences and policies read live as an m365 collector prefill which capabilities
/// Microsoft 365 provides, and an accepted one is routed to that collector, so its checks run for it alone.
/// </summary>
public sealed class OnboardM365Tests
{
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    private static readonly Mitigation[] Mitigations =
    [
        new("ac", [new SnapshotNode("urn:example:ac:1", "ISM-0843", "urn:example:ac", ["1"], "Application control")]),
        new("mfa", [new SnapshotNode("urn:example:mfa:1", "ISM-1504", "urn:example:mfa", ["1"], "MFA")]),
    ];

    private static readonly ScopeSystem[] Systems = [new("Example 365", null, SystemType.Primary, null, null, [], new Dictionary<string, string>(), At)];

    [Fact]
    public void Licences_and_policies_prefill_capabilities()
    {
        var live = new EnvironmentFacts("Microsoft Graph (example-m365)", ["AAD_PREMIUM_P2"], []) { Collector = "example-m365" };

        var proposals = CapabilitySignals.Prefill(Mitigations, Systems, live, microsoftSystem: "Example 365");

        var mfa = proposals.Single(p => p.Mitigation == "mfa");
        (mfa.Suggested, mfa.Route).ShouldBe((CapabilityProvision.System, "example-m365"));
        proposals.Single(p => p.Mitigation == "ac").Route.ShouldBeNull();

        // The person confirms before any check counts: accepting routes the capability to the collector it was read through.
        var accepted = CapabilityAnswers.Decide(mfa, CapabilityAnswer.Accept, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value();
        (accepted.Evidence, accepted.Route).ShouldBe(("collector", "example-m365"));

        // Naming another product instead routes nothing to Microsoft 365.
        var other = CapabilityAnswers.Decide(mfa, CapabilityAnswer.System(["Example identity provider"]), "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value();
        (other.Evidence, other.Route).ShouldBe((null, null));

        // Recorded answers name no collector, so nothing is routed.
        var recorded = CapabilitySignals.Prefill(Mitigations, Systems, live with { Collector = null }, microsoftSystem: "Example 365");
        recorded.Single(p => p.Mitigation == "mfa").Route.ShouldBeNull();
    }
}
