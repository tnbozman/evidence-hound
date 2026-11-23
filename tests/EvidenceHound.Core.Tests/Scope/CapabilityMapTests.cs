using EvidenceHound.Core.Scope;
using static EvidenceHound.Core.Tests.Scope.CapabilityFixture;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s09-28: the capability map decides, once per mitigation and with an approver, which product provides it; a gap is a gap.</summary>
public class CapabilityMapTests
{
    private static readonly Dictionary<string, Frameworks.FrameworkSnapshot> Snapshots = new(StringComparer.Ordinal) { [Framework] = Snapshot };

    [Fact]
    public void Mitigations_are_the_frameworks_requirement_groups_in_order()
    {
        CapabilityMap.Mitigations(Snapshot).Select(m => (m.Code, string.Join(",", m.Requirements.Select(r => r.RefId))))
            .ShouldBe([("ac", "AC-1,AC-2"), ("mfa", "MFA-1"), ("rb", "RB-1"), ("pa", "PA-1")]);
    }

    [Fact]
    public void Every_mitigation_gets_one_decision_with_an_approver()
    {
        CapabilityMap.Validate(ScopeOf(Standard()), Snapshots).ShouldBeEmpty();

        var map = Map(
            Decision("ac", CapabilityProvision.System, "Example app control"),
            Decision("ac", CapabilityProvision.Manual),
            Decision("mfa", CapabilityProvision.System, "Example 365") with { ApprovedBy = null },
            Decision("pa", CapabilityProvision.System),
            Decision("xx", CapabilityProvision.Gap),
            Decision("rb", CapabilityProvision.System, "Example tablets"));

        var problems = CapabilityMap.Validate(ScopeOf(map), Snapshots);

        problems.Select(p => p.Code).ShouldBe(
            ["scope.duplicate", "capability.approver_missing", "capability.systems_missing", "capability.mitigation_unknown"],
            ignoreOrder: true);
        problems.ShouldContain(p => p.Code == "capability.approver_missing" && p.Message.Contains("mfa", StringComparison.Ordinal));
        CapabilityMap.Validate(ScopeOf(Map(Decision("ac", CapabilityProvision.Manual))), Snapshots)
            .Where(p => p.Code == "capability.decision_missing").Select(p => p.Message).ShouldBe(
                ["mitigation 'mfa' has no decision in scope/capabilities.yaml.", "mitigation 'rb' has no decision in scope/capabilities.yaml.", "mitigation 'pa' has no decision in scope/capabilities.yaml."]);
    }

    [Fact]
    public void Systems_named_before_they_are_onboarded_are_pending_not_errors()
    {
        var map = Map(
            Decision("ac", CapabilityProvision.System, "Example app control") with { Supplier = new ScopeRef("SUP-EXAPPCTL", At) },
            Decision("mfa", CapabilityProvision.System, "Example 365"),
            Decision("rb", CapabilityProvision.System, "Example tablets", "Example app control"),
            Decision("pa", CapabilityProvision.Gap));
        var scope = ScopeOf(map) with { Setup = ScopeOf(map).Setup with { Domains = [ScopeOf(map).Setup.Domains[0] with { Systems = [ScopeOf(map).Setup.Domains[0].Systems[0]] }] } };

        CapabilityMap.Validate(scope, Snapshots).Select(p => p.Code).ShouldBe(["scope.reference_unknown"]);
        CapabilityMap.Pending(scope).Select(p => (p.System, string.Join(",", p.Mitigations), p.Supplier)).ShouldBe(
            [("Example app control", "ac,rb", (string?)"SUP-EXAPPCTL"), ("Example tablets", "rb", (string?)null)]);
    }

    [Fact]
    public void A_map_for_a_framework_the_scope_does_not_load_is_refused()
    {
        var map = Standard() with { Framework = new ScopeRef("urn:example:risk:framework:other", At) };

        CapabilityMap.Validate(ScopeOf(map), Snapshots).ShouldHaveSingleItem().Code.ShouldBe("capability.framework_unknown");
    }

    [Fact]
    public void Gap_is_reported_as_a_gap_not_a_fail()
    {
        var rows = CapabilityMap.Rows(ScopeOf(Standard()), Snapshot);

        rows.Gaps.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            gap => gap.Mitigation.ShouldBe("pa"),
            gap => gap.Requirements.ShouldBe(["PA-1"]));
        rows.Audits.SelectMany(a => a.Rows).ShouldNotContain(r => r.Requirement == "PA-1");
    }
}
