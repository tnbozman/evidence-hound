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
