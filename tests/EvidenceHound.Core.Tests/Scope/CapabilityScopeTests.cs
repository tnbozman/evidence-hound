using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using static EvidenceHound.Core.Tests.Scope.CapabilityFixture;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s09-28: no blob. The map's decisions land as not-applicable rows in each system's own audit, naming the system that provides them.</summary>
public class CapabilityScopeTests
{
    [Fact]
    public void Check_for_an_unused_capability_is_not_applicable_with_the_reason()
    {
        var rows = CapabilityMap.Rows(ScopeOf(Standard()), Snapshot);

        var ms365 = rows.Audits.Single(a => a.AuditRefId == "EX365-E8");
        ms365.File.ShouldBe("scope/audits/ex365-e8.yaml");
        ms365.Rows.Select(r => (r.Key, r.Requirement, r.Observation)).ShouldBe(
        [
            ("ref", "AC-1", "Met by Example app control (audit APPCONTROL-E8), as recorded in scope/capabilities.yaml (approved by GRC lead on 2026-11-23); open until that audit shows it met."),
            ("ref", "AC-2", "Met by Example app control (audit APPCONTROL-E8), as recorded in scope/capabilities.yaml (approved by GRC lead on 2026-11-23); open until that audit shows it met."),
            ("ref", "RB-1", "Met by Example backup (audit BACKUP-E8), as recorded in scope/capabilities.yaml (approved by GRC lead on 2026-11-23); open until that audit shows it met."),
        ]);
        ms365.Rows.Select(r => string.Join(", ", r.ProvidedBy)).ShouldBe(["Example app control", "Example app control", "Example backup"]);
    }

    [Fact]
    public void Decisions_land_in_each_systems_own_audit_not_a_combined_one()
    {
        var rows = CapabilityMap.Rows(ScopeOf(Standard()), Snapshot);

        rows.Audits.Select(a => a.AuditRefId).ShouldBe(["EX365-E8", "APPCONTROL-E8", "BACKUP-E8"]);
        rows.Audits.Single(a => a.AuditRefId == "APPCONTROL-E8").Rows.Select(r => r.Requirement).ShouldBe(["MFA-1", "RB-1"]);
        rows.Audits.Single(a => a.AuditRefId == "BACKUP-E8").Rows.Select(r => r.Requirement).ShouldBe(["AC-1", "AC-2", "MFA-1"]);
    }

    [Fact]
    public void Rows_already_written_by_hand_and_groups_the_audit_leaves_out_are_kept_as_they_are()
    {
        var scope = ScopeOf(
            Standard(),
            Audit("EX365-E8", "Example 365", Written("AC-2")) with { Groups = [new ScopeRef("1", At)] },
            Audit("APPCONTROL-E8", "Example app control"),
            Audit("BACKUP-E8", "Example backup"));

        CapabilityMap.Rows(scope, Snapshot).Audits.Single(a => a.AuditRefId == "EX365-E8").Rows.Select(r => r.Requirement).ShouldBe(["AC-1", "RB-1"]);
    }

    [Fact]
    public void A_providing_system_without_an_audit_yet_is_named_as_such()
    {
        var scope = ScopeOf(Standard(), Audit("EX365-E8", "Example 365"));

        CapabilityMap.Rows(scope, Snapshot).Audits.ShouldHaveSingleItem().Rows[0].Observation
            .ShouldStartWith("Met by Example app control (no audit yet; onboard it with 'hound onboard Example app control'),");
    }

    [Fact]
    public void Requirements_the_audits_mapping_computes_are_left_to_the_mapping_and_listed()
    {
        var target = new ScopeTarget("EX365-E8", [RequirementKey.Ref("AC-1")], [], At);

        var ms365 = CapabilityMap.Rows(ScopeOf(Standard()), Snapshot, [target]).Audits.Single(a => a.AuditRefId == "EX365-E8");

        ms365.Rows.Select(r => r.Requirement).ShouldBe(["AC-2", "RB-1"]);
        ms365.Computed.ShouldBe(["AC-1"]);
    }
}
