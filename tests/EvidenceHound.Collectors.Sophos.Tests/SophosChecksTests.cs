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

public class SophosSuperAdminCheckTests
{
    private static readonly SophosOptions Options = Fixtures.Options();

    private static readonly string SuperAdmin = "00000000-0000-4000-8000-00000000a501";

    [Fact]
    public void Super_admins_within_the_maximum_pass()
    {
        var finding = SophosChecks.SuperAdminCount(Fixtures.Recorded(), Options, Fixtures.Now);

        finding.CheckId.ShouldBe("network.management.super_admin_count");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("1 of 3 Sophos Central admins holds Super Admin; at most 2 allowed.");
        finding.Sources.ShouldBe(["admins", "roles"]);
    }

    [Fact]
    public void More_super_admins_than_allowed_fail()
    {
        var data = Fixtures.Recorded(items =>
        {
            foreach (var admin in items["admins"].OfType<JsonObject>())
            {
                admin["roleAssignments"] = new JsonArray(new JsonObject { ["id"] = "00000000-0000-4000-8000-00000000af09", ["roleId"] = SuperAdmin });
            }
        });

        var finding = SophosChecks.SuperAdminCount(data, Options, Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("3 of 3 Sophos Central admins hold Super Admin; at most 2 allowed.");
    }

    [Fact]
    public void The_maximum_is_a_setting()
    {
        var finding = SophosChecks.SuperAdminCount(Fixtures.Recorded(), Fixtures.Options(s => s["super_admin_max"] = "0"), Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("at most 0 allowed");
    }

    [Fact]
    public void An_admin_with_super_admin_twice_counts_once()
    {
        var data = Fixtures.Recorded(items => items.At("admins", 0)["roleAssignments"]!.AsArray()
            .Add(new JsonObject { ["id"] = "00000000-0000-4000-8000-00000000af08", ["roleId"] = SuperAdmin }));

        SophosChecks.SuperAdminCount(data, Options, Fixtures.Now).Observation.ShouldStartWith("1 of 3");
    }

    [Theory]
    [InlineData("super admin")]
    [InlineData("SuperAdmin")]
    public void The_super_admin_role_is_found_by_its_name_in_any_case_or_spacing(string name)
    {
        var data = Fixtures.Recorded(items => items.At("roles", 0)["name"] = name);

        SophosChecks.SuperAdminCount(data, Options, Fixtures.Now).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void A_custom_role_with_the_same_name_is_not_super_admin()
    {
        var data = Fixtures.Recorded(items => items.At("roles", 0)["type"] = "custom");

        var finding = SophosChecks.SuperAdminCount(data, Options, Fixtures.Now);

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldContain("no predefined Super Admin role");
    }

    [Fact]
    public void No_super_admin_role_in_the_roles_list_is_an_error_not_a_pass()
    {
        var data = Fixtures.Recorded(items => items["roles"] = new JsonArray());

        SophosChecks.SuperAdminCount(data, Options, Fixtures.Now).Outcome.ShouldBe(CheckOutcome.Error);
    }
}
