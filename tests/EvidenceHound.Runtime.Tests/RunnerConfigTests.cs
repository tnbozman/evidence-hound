using EvidenceHound.Collectors;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Tests;

/// <summary>No model in evidence collection: the runner refuses a deployment config that sets up a language model or reaches for its key.</summary>
public sealed class RunnerConfigTests
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
          - name: example
            type: rest
            schedule: "0 2 * * *"
            secrets:
              client_secret: file:/run/secrets/example_client_secret
        target:
          mapping: ./mapping.yaml
          compliance_assessment: EXAMPLE-1
        """;

    [Fact]
    public void Llm_key_or_setting_in_runner_config_is_refused()
    {
        ConfigLoader.Parse(Config, Source, Known).ShouldBeOfType<Core.Results.Result<DeploymentConfig>.Ok>();

        var withLlm = ConfigLoader.Parse(Config + "\nllm:\n  provider: anthropic\n", Source, Known).Problems().ShouldHaveSingleItem();
        withLlm.Code.ShouldBe("config.llm_in_runner");
        withLlm.Message.ShouldContain("hound.yaml line 15");
        withLlm.Message.ShouldContain("onboarding.yaml");

        var withKey = ConfigLoader.Parse(Config.Replace("file:/run/secrets/example_client_secret", "file:/home/example/.config/hound/secrets/llm_api_key", StringComparison.Ordinal), Source, Known)
            .Problems().ShouldHaveSingleItem();
        withKey.Code.ShouldBe("config.llm_in_runner");
        withKey.Message.ShouldContain("hound.yaml line 11");
    }
}
