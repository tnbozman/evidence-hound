using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class ConditionalAccessTests
{
    private static readonly string RequireMfa = "00000000-0000-4000-8000-00000000ca01";

    private static readonly string BlockLegacy = "00000000-0000-4000-8000-00000000ca02";

    private static readonly string AdminStrength = "00000000-0000-4000-8000-00000000ca03";

    private static readonly string Unknown = "00000000-0000-4000-8000-0000000000c1";

    [Fact]
    public void Enforced_with_only_break_glass_exclusions_passes()
    {
        var finding = MfaChecks.EnforcedByConditionalAccess(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.mfa.enforced_conditional_access");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Policy 'Require MFA for all users' requires MFA for all users and all cloud apps, excluding only the 2 documented break-glass accounts.");
        finding.Sources.ShouldBe(["conditional-access-policies"]);
    }

    [Fact]
    public void Enforced_excluded_unknown_account_fails()
    {
        var tenant = Recorded(items => items.Policy(RequireMfa)["conditions"]!["users"]!["excludeUsers"]!.AsArray().Add(Unknown));

        var finding = MfaChecks.EnforcedByConditionalAccess(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe($"Policy 'Require MFA for all users' requires MFA for all users but also excludes 1 account or group not documented as break-glass: {Unknown}.");
    }

    [Fact]
    public void Enforced_excluded_group_fails()
    {
        var tenant = Recorded(items => items.Policy(RequireMfa)["conditions"]!["users"]!["excludeGroups"] = Strings("00000000-0000-4000-8000-0000000000f1"));

        MfaChecks.EnforcedByConditionalAccess(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Enforced_report_only_policy_fails()
    {
        var tenant = Recorded(items => items.Policy(RequireMfa)["state"] = "enabledForReportingButNotEnforced");

        var finding = MfaChecks.EnforcedByConditionalAccess(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No enabled Conditional Access policy requires MFA for all users and all cloud apps.");
    }

    [Fact]
    public void Enforced_mfa_or_another_control_fails()
    {
        var tenant = Recorded(items => items.Policy(RequireMfa)["grantControls"]!["builtInControls"] = Strings("mfa", "compliantDevice"));

        MfaChecks.EnforcedByConditionalAccess(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Enforced_by_an_authentication_strength_passes()
    {
        var tenant = Recorded(items =>
        {
            items.Policy(RequireMfa)["grantControls"]!["builtInControls"] = Strings();
            items.Policy(RequireMfa)["grantControls"]!["authenticationStrength"] = new System.Text.Json.Nodes.JsonObject { ["id"] = "00000000-0000-0000-0000-000000000002" };
        });

        MfaChecks.EnforcedByConditionalAccess(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Legacy_auth_blocked_passes()
    {
        var finding = MfaChecks.LegacyAuthBlocked(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.legacy_auth_blocked");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Policy 'Block legacy authentication' blocks Exchange ActiveSync and other legacy clients for all users, excluding only the 2 documented break-glass accounts.");
    }

    [Fact]
    public void Legacy_auth_policy_missing_a_client_type_fails()
    {
        var tenant = Recorded(items => items.Policy(BlockLegacy)["conditions"]!["clientAppTypes"] = Strings("other"));

        var finding = MfaChecks.LegacyAuthBlocked(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No enabled Conditional Access policy blocks legacy authentication clients (Exchange ActiveSync and other clients) for all users.");
    }

    [Fact]
    public void Legacy_auth_excluded_unknown_account_fails()
    {
        var tenant = Recorded(items => items.Policy(BlockLegacy)["conditions"]!["users"]!["excludeUsers"]!.AsArray().Add(Unknown));

        var finding = MfaChecks.LegacyAuthBlocked(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain(Unknown);
    }

    [Fact]
    public void Phishing_resistant_for_every_admin_role_passes()
    {
        var finding = MfaChecks.PhishingResistantAdmins(Recorded(), Options());

        finding.CheckId.ShouldBe("identity.mfa.phishing_resistant_admins");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("A phishing-resistant authentication strength is required for all 14 privileged roles.");
    }

    [Fact]
    public void Phishing_resistant_for_some_admin_roles_partial()
    {
        var tenant = Recorded(items => items.Policy(AdminStrength)["conditions"]!["users"]!["includeRoles"] = Strings(PrivilegedRoles.GlobalAdministrator));

        var finding = MfaChecks.PhishingResistantAdmins(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldStartWith("A phishing-resistant authentication strength is required for 1 of 14 privileged roles. Not covered: Privileged Role Administrator, ");
    }

    [Fact]
    public void Phishing_resistant_missing_fails()
    {
        var tenant = Recorded(items => items.Policy(AdminStrength)["grantControls"]!["authenticationStrength"] = null);

        var finding = MfaChecks.PhishingResistantAdmins(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No enabled Conditional Access policy requires a phishing-resistant authentication strength for privileged roles.");
    }

    [Fact]
    public void Phishing_resistant_for_all_users_covers_admin_roles()
    {
        var tenant = Recorded(items =>
        {
            items.Policy(AdminStrength)["conditions"]!["users"]!["includeRoles"] = Strings();
            items.Policy(AdminStrength)["conditions"]!["users"]!["includeUsers"] = Strings("All");
        });

        MfaChecks.PhishingResistantAdmins(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Phishing_resistant_roles_are_configurable()
    {
        var options = Options(s => s["privileged_roles"] = PrivilegedRoles.GlobalAdministrator);
        var tenant = Recorded(items => items.Policy(AdminStrength)["conditions"]!["users"]!["includeRoles"] = Strings(PrivilegedRoles.GlobalAdministrator));

        MfaChecks.PhishingResistantAdmins(tenant, options).Observation.ShouldBe("A phishing-resistant authentication strength is required for all 1 privileged roles.");
    }
}
