using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365.Tests;

public class MfaRegistrationTests
{
    [Fact]
    public void Registered_all_users_passes()
    {
        var finding = MfaChecks.RegisteredAllUsers(Tenants.Recorded(), Tenants.Options());

        finding.CheckId.ShouldBe("identity.mfa.registered_all_users");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("All 7 enabled member accounts are MFA-capable.");
        finding.Sources.ShouldBe(["users", "mfa-registration"]);
    }

    [Fact]
    public void Registered_below_100_percent_above_threshold_partial()
    {
        var tenant = Tenants.Recorded(items => items.Item("mfa-registration", "00000000-0000-4000-8000-0000000000c3")["isMfaCapable"] = false);

        var finding = MfaChecks.RegisteredAllUsers(tenant, Tenants.Options(s => s["mfa_partial_percent"] = "80"));

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("6 of 7 enabled member accounts (85.7 %) are MFA-capable; 1 is not. Partial at 80 % or more.");
    }

    [Fact]
    public void Registered_below_threshold_fails()
    {
        var tenant = Tenants.Recorded(items => items.Item("mfa-registration", "00000000-0000-4000-8000-0000000000c3")["isMfaCapable"] = false);

        var finding = MfaChecks.RegisteredAllUsers(tenant, Tenants.Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldStartWith("6 of 7 enabled member accounts (85.7 %) are MFA-capable; 1 is not.");
    }

    [Fact]
    public void Registered_account_missing_from_the_report_is_not_capable()
    {
        var tenant = Tenants.Recorded(items => items["mfa-registration"].Remove(items.Item("mfa-registration", "00000000-0000-4000-8000-0000000000c3")));

        MfaChecks.RegisteredAllUsers(tenant, Tenants.Options()).Observation.ShouldStartWith("6 of 7");
    }

    [Fact]
    public void Registered_ignores_guests_and_disabled_accounts()
    {
        // The recorded guest and disabled account are not MFA-capable, yet the check passes.
        MfaChecks.RegisteredAllUsers(Tenants.Recorded(), Tenants.Options()).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Registered_with_no_enabled_members_fails()
    {
        var tenant = Tenants.Recorded(items => items["users"].Clear());

        var finding = MfaChecks.RegisteredAllUsers(tenant, Tenants.Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("No enabled member accounts");
    }
}
