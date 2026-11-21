using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class DeviceChecksTests
{
    private static readonly string Lt3 = "00000000-0000-4000-8000-00000000d003";

    private static readonly string Lt4 = "00000000-0000-4000-8000-00000000d004";

    [Fact]
    public void Compliant_ratio_all_compliant_passes()
    {
        var finding = DeviceChecks.CompliantRatio(Recorded(), Options());

        finding.CheckId.ShouldBe("endpoint.managed.compliant_ratio");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("5 of 5 Intune-managed Windows devices (100 %) are compliant. Pass at 95 %, partial at 80 %.");
        finding.Sources.ShouldBe(["managed-devices"]);
    }

    [Fact]
    public void Compliant_ratio_at_partial_threshold_partial()
    {
        var tenant = Recorded(items => items.Item("managed-devices", Lt3)["complianceState"] = "noncompliant");

        var finding = DeviceChecks.CompliantRatio(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldStartWith("4 of 5 Intune-managed Windows devices (80 %) are compliant.");
    }

    [Fact]
    public void Compliant_ratio_below_partial_threshold_fails()
    {
        var tenant = Recorded(items =>
        {
            items.Item("managed-devices", Lt3)["complianceState"] = "noncompliant";
            items.Item("managed-devices", Lt4)["complianceState"] = "inGracePeriod";
        });

        DeviceChecks.CompliantRatio(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Compliant_ratio_thresholds_are_configurable()
    {
        var tenant = Recorded(items => items.Item("managed-devices", Lt3)["complianceState"] = "noncompliant");

        var finding = DeviceChecks.CompliantRatio(tenant, Options(s => s["compliant_pass_percent"] = "80"));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Compliant_ratio_with_no_windows_devices_fails()
    {
        var tenant = Recorded(items => items["managed-devices"].Clear());

        var finding = DeviceChecks.CompliantRatio(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No Intune-managed Windows devices were found.");
    }

    [Fact]
    public void Supported_versions_all_supported_passes()
    {
        var finding = DeviceChecks.SupportedVersions(Recorded(), Options());

        finding.CheckId.ShouldBe("endpoint.os.supported_versions");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("All 5 Intune-managed Windows devices run a supported build (10.0.22631, 10.0.26100).");
    }

    [Fact]
    public void Supported_versions_unsupported_build_fails()
    {
        var tenant = Recorded(items =>
        {
            items.Item("managed-devices", Lt3)["osVersion"] = "10.0.19045.5131";
            items.Item("managed-devices", Lt4)["osVersion"] = "10.0.19045.5011";
        });

        var finding = DeviceChecks.SupportedVersions(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("2 of 5 Intune-managed Windows devices run an unsupported build: 10.0.19045 (2). Supported: 10.0.22631, 10.0.26100.");
    }

    [Fact]
    public void Supported_versions_with_no_windows_devices_fails()
    {
        var tenant = Recorded(items => items["managed-devices"].Clear());

        DeviceChecks.SupportedVersions(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }
}
