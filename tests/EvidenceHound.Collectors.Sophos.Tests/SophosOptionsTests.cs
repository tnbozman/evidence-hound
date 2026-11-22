namespace EvidenceHound.Collectors.Sophos.Tests;

public class SophosOptionsTests
{
    [Fact]
    public void Defaults_point_at_sophos_central()
    {
        var options = Fixtures.Options();

        options.IdUrl.ShouldBe(new Uri("https://id.sophos.com/"));
        options.ApiUrl.ShouldBe(new Uri("https://api.central.sophos.com/"));
        options.TenantId.ShouldBeNull();
        options.SupportedFirmware.ShouldBe(["20.0", "21.0"]);
        options.IncludeIdentities.ShouldBeFalse();
        options.SuperAdminMax.ShouldBe(2);
    }

    [Fact]
    public void Client_id_and_supported_firmware_are_required()
    {
        var problems = SophosOptions.FromSettings(new CollectorSettings(new Dictionary<string, string>())).Problems();

        problems.Select(p => p.Code).ShouldBe(["sophos.setting_missing", "sophos.setting_missing"]);
        problems.Select(p => p.Message).ShouldContain(m => m.Contains("client_id"));
        problems.Select(p => p.Message).ShouldContain(m => m.Contains("supported_firewall_firmware"));
    }

    [Theory]
    [InlineData("supported_firewall_firmware", "latest")]
    [InlineData("api_url", "ftp://api.example.com")]
    [InlineData("super_admin_max", "two")]
    [InlineData("super_admin_max", "-1")]
    public void Invalid_settings_are_problems(string name, string value)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["client_id"] = "c", ["supported_firewall_firmware"] = "21.0", [name] = value };

        var problem = SophosOptions.FromSettings(new CollectorSettings(settings)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.setting_invalid");
        problem.Message.ShouldContain(name);
    }

    [Fact]
    public void Settings_of_the_removed_endpoint_checks_are_ignored_so_older_configs_still_load()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = "c",
            ["supported_firewall_firmware"] = "21.0",
            ["stale_agent_days"] = "soon",
            ["healthy_pass_percent"] = "120",
            ["healthy_partial_percent"] = "90",
        };

        SophosOptions.FromSettings(new CollectorSettings(settings)).Value().SupportedFirmware.ShouldBe(["21.0"]);
    }
}
