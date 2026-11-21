using EvidenceHound.Collectors;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Tests;

public class DescribeTests
{
    private static readonly Dictionary<string, CollectorDescription> Described = new(StringComparer.Ordinal)
    {
        ["widget"] = new(
            ["widget.patched"],
            [new("site", "The widget site to read.", Required: true), new("region", "Optional region.", Required: false)],
            [new("api_key", "Reference to the widget API key.", Required: true)]),
    };

    private static string Config(string collector) => $$"""
        ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
        evidence: {root: ./evidence}
        collectors:
          - name: widgets
            type: widget
            schedule: "0 2 * * *"
        {{collector}}
        target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
        """;

    [Fact]
    public void Config_validation_uses_described_settings()
    {
        var problems = ConfigLoader.Parse(Config(""), "hound.yaml", Described).Problems();

        problems.Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["config.secret_missing", "config.setting_missing"]);
        problems.ShouldContain(p => p.Code == "config.setting_missing" && p.Message == "hound.yaml line 4: collector type 'widget' needs setting 'site'.");
        problems.ShouldContain(p => p.Code == "config.secret_missing" && p.Message == "hound.yaml line 4: collector type 'widget' needs secret 'api_key'.");
    }

    [Fact]
    public void Path_settings_are_resolved_against_the_config_files_folder()
    {
        var described = new Dictionary<string, CollectorDescription>(StringComparer.Ordinal)
        {
            ["widget"] = new(["widget.patched"], [new("drop", "A folder.", Required: true, IsPath: true), new("site", "Not a path.", Required: false)], []),
        };
        var source = Path.Combine(Path.GetTempPath(), "hound-example", "hound.yaml");

        var collector = ConfigLoader.Parse(Config("    settings: {drop: ./manual/firewall, site: ./not-a-path}"), source, described).Value().Collectors.ShouldHaveSingleItem();

        collector.Settings["drop"].ShouldBe(Path.Combine(Path.GetTempPath(), "hound-example", "manual", "firewall"));
        collector.Settings["site"].ShouldBe("./not-a-path");
    }

    [Fact]
    public void Config_with_every_required_setting_and_secret_is_valid()
    {
        var yaml = Config("""
                settings: {site: example.com}
                secrets: {api_key: "env:HOUND_WIDGET_KEY"}
            """);

        var collector = ConfigLoader.Parse(yaml, "hound.yaml", Described).Value().Collectors.ShouldHaveSingleItem();

        collector.Settings["site"].ShouldBe("example.com");
    }
}
