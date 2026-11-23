using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s09-29: which capabilities a collector instance answers, from scope/capabilities.yaml, and who answers the rest.</summary>
public class CapabilityRoutingTests
{
    private static readonly SourceLine At = new("scope/capabilities.yaml", 1);

    [Fact]
    public void A_capability_routed_to_another_product_or_left_manual_or_a_gap_is_provided_elsewhere()
    {
        var scope = Scope(
            Decision("mfa", CapabilityProvision.System, "example-m365", "Example 365"),
            Decision("ac", CapabilityProvision.System, "example-app-control", "Example app control"),
            Decision("rb", CapabilityProvision.Manual, null),
            Decision("pa", CapabilityProvision.Gap, null),
            Decision("rap", CapabilityProvision.System, null, "Example 365"),
            Decision("uah", CapabilityProvision.System, null, "Example browser"));

        var routed = CapabilityRouting.For(scope, "example-m365", system: "Example 365");

        routed.Keys.Order(StringComparer.Ordinal).ShouldBe(["ac", "pa", "rb", "uah"]);
        routed["ac"].ShouldBe("Example app control (audit EX-APPCTL-E8-2026)");
        routed["rb"].ShouldBe("manual evidence");
        routed["pa"].ShouldContain("no product yet");
        routed["uah"].ShouldBe("Example browser (no audit yet)");
    }

    [Fact]
    public void Without_the_instances_system_only_a_route_to_another_collector_counts()
    {
        var scope = Scope(
            Decision("ac", CapabilityProvision.System, "example-app-control", "Example app control"),
            Decision("uah", CapabilityProvision.System, null, "Example browser"));

        CapabilityRouting.For(scope, "example-m365", system: null).Keys.ShouldBe(["ac"]);
    }

    [Fact]
    public void Without_a_map_everything_is_answered_here()
    {
        CapabilityRouting.For(Scope(), "example-m365", "Example 365").ShouldBeEmpty();
        CapabilityRouting.For(null, "example-m365", "Example 365").ShouldBeEmpty();
    }

    private static ScopeCapability Decision(string mitigation, CapabilityProvision provision, string? route, params string[] systems) =>
        new(mitigation, provision, At) { Route = route, Systems = [.. systems.Select(s => new ScopeRef(s, At))], ApprovedBy = "GRC lead" };

    private static ScopeDocument Scope(params ScopeCapability[] decisions)
    {
        var framework = new ScopeRef("urn:example:framework", At);
        var audit = new ScopeAudit("EX-APPCTL-E8-2026", "Example app control", framework, [], new ScopeRef("Example Pty Ltd", At), new ScopeRef("Example baseline", At), At) { Systems = [new ScopeRef("Example app control", At)] };
        return new ScopeDocument(new ScopeSetup("Example Pty Ltd", [], []), [], [audit])
        {
            Capabilities = decisions.Length == 0 ? null : new ScopeCapabilityMap(framework, decisions, At),
        };
    }
}
