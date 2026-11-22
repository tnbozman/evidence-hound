using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Sophos.Tests;

public class SophosChecksTests
{
    private static readonly SophosOptions Options = Fixtures.Options();

    [Fact]
    public void Recorded_tenant_passes_every_check()
    {
        var data = Fixtures.Recorded();

        SophosChecks.All.Select(c => c.Evaluate(data, Options, Fixtures.Now)).ShouldAllBe(f => f.Outcome == CheckOutcome.Pass);
    }

    [Fact]
    public void Healthy_ratio_counts_endpoints_in_good_health()
    {
        var finding = SophosChecks.HealthyRatio(Fixtures.Recorded(), Options, Fixtures.Now);

        finding.CheckId.ShouldBe("endpoint.protection.healthy_ratio");
        finding.Observation.ShouldContain("5 of 5 endpoints");
        finding.Sources.ShouldBe(["endpoints"]);
    }

    [Theory]
    [InlineData(1, CheckOutcome.Partial)]
    [InlineData(2, CheckOutcome.Fail)]
    public void Healthy_ratio_is_partial_then_failed_as_health_drops(int unhealthy, CheckOutcome expected)
    {
        var data = Fixtures.Recorded(items =>
        {
            for (var i = 0; i < unhealthy; i++)
            {
                items.At("endpoints", i)["health"]!["overall"] = i == 0 ? "suspicious" : "bad";
            }
        });

        var finding = SophosChecks.HealthyRatio(data, Fixtures.Options(s => s["healthy_partial_percent"] = "75"), Fixtures.Now);

        finding.Outcome.ShouldBe(expected);
        finding.Observation.ShouldContain("suspicious (1)");
    }

    [Fact]
    public void Tamper_protection_must_be_on_for_every_endpoint()
    {
        var data = Fixtures.Recorded(items =>
        {
            items.At("endpoints", 1)["tamperProtectionEnabled"] = false;
            items.At("endpoints", 2).Remove("tamperProtectionEnabled");
        });

        var finding = SophosChecks.TamperProtection(data, Options, Fixtures.Now);

        finding.CheckId.ShouldBe("endpoint.protection.tamper_protection_enabled");
        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("2 of 5 endpoints");
    }

    [Fact]
    public void An_agent_not_seen_for_more_than_the_threshold_is_stale()
    {
        var data = Fixtures.Recorded(items => items.At("endpoints", 4)["lastSeenAt"] = "2026-10-01T00:00:00.000Z");

        var finding = SophosChecks.StaleAgents(data, Options, Fixtures.Now);

        finding.CheckId.ShouldBe("endpoint.protection.stale_agents");
        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("1 of 5 endpoints");
        finding.Observation.ShouldContain("14 days");
    }

    [Fact]
    public void Stale_agent_threshold_is_configurable()
    {
        var finding = SophosChecks.StaleAgents(Fixtures.Recorded(), Fixtures.Options(s => s["stale_agent_days"] = "1"), Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("1 of 5 endpoints");
    }

    [Fact]
    public void An_endpoint_never_seen_is_stale()
    {
        var finding = SophosChecks.StaleAgents(Fixtures.Recorded(items => items.At("endpoints", 0).Remove("lastSeenAt")), Options, Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void No_endpoints_fails_the_endpoint_checks()
    {
        var data = Fixtures.Recorded(items => items["endpoints"] = []);

        new[] { SophosChecks.HealthyRatio, SophosChecks.TamperProtection, SophosChecks.StaleAgents }
            .Select(check => check(data, Options, Fixtures.Now))
            .ShouldAllBe(f => f.Outcome == CheckOutcome.Fail && f.Observation.Contains("No endpoints"));
    }

    [Fact]
    public void Firewall_on_a_supported_firmware_passes()
    {
        var finding = SophosChecks.FirmwareSupported(Fixtures.Recorded(), Options, Fixtures.Now);

        finding.CheckId.ShouldBe("network.firewall.firmware_supported");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldContain("21.0.1");
        finding.Sources.ShouldBe(["firewalls"]);
    }

    [Theory]
    [InlineData("SFOS 19.5.3 MR-3-Build652")]
    [InlineData("SFOS 210.1 GA")]
    [InlineData(null)]
    public void Firewall_on_another_firmware_fails(string? firmware)
    {
        var data = Fixtures.Recorded(items =>
        {
            if (firmware is null)
            {
                items.At("firewalls", 0).Remove("firmwareVersion");
            }
            else
            {
                items.At("firewalls", 0)["firmwareVersion"] = firmware;
            }
        });

        var finding = SophosChecks.FirmwareSupported(data, Options, Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("20.0, 21.0");
    }

    [Fact]
    public void No_firewalls_is_not_applicable()
    {
        var finding = SophosChecks.FirmwareSupported(Fixtures.Recorded(items => items["firewalls"] = new JsonArray()), Options, Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.NotApplicable);
    }
}
