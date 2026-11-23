using EvidenceHound.Collectors;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Tests;

/// <summary>Collector types that hound no longer has point the operator at what replaced them.</summary>
public sealed class CollectorConfigTests
{
    private static readonly Dictionary<string, CollectorDescription> Known = new(StringComparer.Ordinal)
    {
        ["rest"] = new([], [], []),
    };

    private static readonly string Source = Path.Combine(Path.GetTempPath(), "deploy", "hound.yaml");

    private static readonly string Config = """
        ciso_assistant:
          url: http://localhost:8000/api/
          token: env:HOUND_CA_TOKEN
        evidence:
          root: ./evidence
        collectors:
          - name: sophos-example
            type: sophos
            schedule: "0 2 * * *"
            settings:
              client_id: 00000000-0000-4000-8000-0000000050d1
              supported_firewall_firmware: "21.0"
            secrets:
              client_secret: file:/run/secrets/sophos_client_secret
        target:
          mapping: ./mapping.yaml
          compliance_assessment: EXAMPLE-1
        """;

    [Fact]
    public void Sophos_type_suggests_rest_profile()
    {
        var problem = ConfigLoader.Parse(Config, Source, Known).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("config.collector_type_retired");
        problem.Message.ShouldContain("type: rest");
        problem.Message.ShouldContain("profile: sophos-central");
        problem.Message.ShouldContain("docs/collectors/sophos.md");
    }
}
