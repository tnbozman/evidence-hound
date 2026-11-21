using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class PrivilegedAccessTests
{
    private static readonly string Ga = PrivilegedRoles.GlobalAdministrator;

    private static readonly string Helpdesk = "729827e3-9c14-49f7-bb1b-9608f156bbb8";

    private static readonly string Kim = "00000000-0000-4000-8000-0000000000c2";

    private static readonly string Sam = "00000000-0000-4000-8000-0000000000a2";

    [Fact]
    public void Global_admin_count_within_range_passes()
    {
        var finding = PrivilegedChecks.GlobalAdminCount(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.privileged.global_admin_count");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("4 accounts hold Global Administrator (2 active, 2 eligible only); expected 2 to 4.");
        finding.Sources.ShouldBe(["role-assignments", "role-eligibility-schedules"]);
    }

    [Fact]
    public void Global_admin_count_above_max_fails()
    {
        var tenant = Recorded(items => items["role-assignments"].Add(Assignment(Kim, Ga)));

        var finding = PrivilegedChecks.GlobalAdminCount(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("5 accounts hold Global Administrator (3 active, 2 eligible only); expected 2 to 4.");
    }

    [Fact]
    public void Global_admin_count_below_min_fails()
    {
        var finding = PrivilegedChecks.GlobalAdminCount(Recorded(), Options(s => { s["global_admin_min"] = "5"; s["global_admin_max"] = "6"; }));

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldEndWith("expected 5 to 6.");
    }

    [Fact]
    public void Global_admin_count_without_pim_counts_active_assignments()
    {
        var finding = PrivilegedChecks.GlobalAdminCount(WithoutPim(), Options());

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("2 accounts hold Global Administrator (2 active, 0 eligible only); expected 2 to 4.");
        finding.Sources.ShouldBe(["role-assignments"]);
    }

    [Fact]
    public void Admins_cloud_only_passes()
    {
        var finding = PrivilegedChecks.AdminsCloudOnly(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.privileged.admins_cloud_only");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("All 4 accounts holding privileged roles are cloud-only.");
        finding.Sources.ShouldBe(["users", "role-assignments", "role-eligibility-schedules"]);
    }

    [Fact]
    public void Admins_cloud_only_synced_admin_fails()
    {
        var tenant = Recorded(items => items["role-eligibility-schedules"].Add(Assignment(Kim, Helpdesk)));

        var finding = PrivilegedChecks.AdminsCloudOnly(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe($"1 of 5 accounts holding privileged roles is synchronised from on-premises: {Kim}.");
    }

    [Fact]
    public void Admins_cloud_only_notes_principals_that_are_not_users()
    {
        var tenant = Recorded(items => items["role-assignments"].Add(Assignment("00000000-0000-4000-8000-0000000000f9", Helpdesk)));

        var finding = PrivilegedChecks.AdminsCloudOnly(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("All 4 accounts holding privileged roles are cloud-only. 1 privileged role holder is a group or app, not assessed.");
    }

    [Fact]
    public void Pim_eligible_not_permanent_passes()
    {
        var finding = PrivilegedChecks.PimEligibleNotPermanent(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.privileged.pim_eligible_not_permanent");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("No permanent privileged role assignments apart from the 2 documented break-glass accounts; 4 privileged role assignments are eligible through PIM.");
        finding.Sources.ShouldBe(["role-assignment-schedule-instances", "role-eligibility-schedules"]);
    }

    [Fact]
    public void Pim_permanent_assignment_fails()
    {
        var tenant = Recorded(items => items["role-assignment-schedule-instances"].Add(Instance(Sam, Ga, "Assigned", end: null)));

        var finding = PrivilegedChecks.PimEligibleNotPermanent(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe($"1 privileged role assignment is permanent rather than eligible: Global Administrator ({Sam}).");
    }

    [Fact]
    public void Pim_time_bound_assignment_passes()
    {
        var tenant = Recorded(items => items["role-assignment-schedule-instances"].Add(Instance(Sam, Ga, "Assigned", end: "2026-12-01T00:00:00Z")));

        PrivilegedChecks.PimEligibleNotPermanent(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Pim_not_licensed_is_not_applicable()
    {
        var finding = PrivilegedChecks.PimEligibleNotPermanent(WithoutPim(), Options());

        finding.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        finding.Observation.ShouldBe("Privileged Identity Management is not licensed in this tenant (it needs Microsoft Entra ID P2 or ID Governance), so roles cannot be made eligible.");
        finding.Sources.ShouldBe(["role-assignments"]);
    }

    [Fact]
    public void Global_admin_range_is_validated()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant_id"] = RecordedTenant.TenantId,
            ["client_id"] = RecordedTenant.ClientId,
            ["supported_windows_builds"] = RecordedTenant.SupportedBuilds,
            ["global_admin_min"] = "5",
            ["global_admin_max"] = "2",
        };

        var problem = M365Options.FromSettings(new CollectorSettings(settings)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.setting_invalid");
        problem.Message.ShouldContain("global_admin_min");
    }

    private static TenantData WithoutPim()
    {
        var items = RecordedTenant.Items();
        var collections = items.ToDictionary(pair => pair.Key, pair => (JsonArray?)pair.Value, StringComparer.Ordinal);
        collections["role-eligibility-schedules"] = null;
        collections["role-assignment-schedule-instances"] = null;
        return TenantData.Parse(collections);
    }

    private static JsonObject Assignment(string principal, string role) =>
        new() { ["id"] = $"test-{principal}-{role}", ["principalId"] = principal, ["roleDefinitionId"] = role, ["directoryScopeId"] = "/" };

    private static JsonObject Instance(string principal, string role, string type, string? end) =>
        new() { ["id"] = $"test-{principal}-{role}", ["principalId"] = principal, ["roleDefinitionId"] = role, ["assignmentType"] = type, ["endDateTime"] = end };
}
