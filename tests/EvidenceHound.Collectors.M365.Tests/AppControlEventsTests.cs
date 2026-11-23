using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class AppControlEventsTests
{
    [Fact]
    public void Events_and_onboarding_above_threshold_passes()
    {
        var finding = AppControlChecks.EventsCentrallyLogged(Recorded(), Options());

        finding.CheckId.ShouldBe("endpoint.application_control.events_centrally_logged");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Defender for Endpoint received 16 application control events from 3 devices in the last 30 days (4 blocked, 12 audited), and 5 of 5 Intune-managed Windows devices (100 %) are onboarded. Most blocked: example-portable-tool.exe (3).");
        finding.Sources.ShouldBe(["hunting-app-control-events", "hunting-app-control-blocked-files", "hunting-defender-onboarding", "managed-devices"]);
    }

    [Fact]
    public void Low_onboarding_is_partial()
    {
        var tenant = Recorded(items => items["hunting-defender-onboarding"][0]!["Devices"] = 4);

        var finding = AppControlChecks.EventsCentrallyLogged(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldStartWith("Only 4 of 5 Intune-managed Windows devices (80 %) are onboarded to Defender for Endpoint, below 95 %;");
    }

    [Fact]
    public void No_events_fails()
    {
        var tenant = Recorded(items => items["hunting-app-control-events"].Clear());

        var finding = AppControlChecks.EventsCentrallyLogged(tenant, Options(s => s["application_control_window_days"] = "7"));

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("Defender for Endpoint received no application control events in the last 7 days, so allowed and blocked executions are not reaching the central log.");
    }

    [Fact]
    public void Missing_licence_is_not_applicable_with_reason()
    {
        var tenant = Recorded() with
        {
            Unavailable = new Dictionary<string, Problem>(StringComparer.Ordinal) { ["hunting-app-control-events"] = new(GraphClient.NotLicensed, "The tenant is not licensed for v1.0/security/runHuntingQuery (400).") },
        };

        var finding = AppControlChecks.EventsCentrallyLogged(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        finding.Observation.ShouldBe("Not applicable: the tenant has no licence that includes advanced hunting (The tenant is not licensed for v1.0/security/runHuntingQuery (400).). Use the manual application control event log template instead.");
    }

    [Fact]
    public void Missing_permission_is_unable_to_assess()
    {
        var tenant = Recorded() with
        {
            Unavailable = new Dictionary<string, Problem>(StringComparer.Ordinal) { ["hunting-defender-onboarding"] = new("m365.graph_forbidden", "Microsoft Graph refused v1.0/security/runHuntingQuery (403). The app registration needs the application permission ThreatHunting.Read.All with admin consent.") },
        };

        var finding = AppControlChecks.EventsCentrallyLogged(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldContain("ThreatHunting.Read.All");
    }
}
