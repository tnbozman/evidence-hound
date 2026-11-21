using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Tests;

public class ConfigTests
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "fake", "manual" };

    private static readonly string Source = Path.Combine(Path.GetTempPath(), "deploy", "hound.yaml");

    private static readonly string Valid = """
        ciso_assistant:
          url: http://localhost:8000/api/
          token: env:HOUND_CA_TOKEN
        evidence:
          root: ./evidence
          retention_days: 30
        collectors:
          - name: idp
            type: fake
            schedule: "0 2 * * *"
            settings:
              tenant: example.com
            secrets:
              api_token: file:/run/secrets/idp_token
        target:
          mapping: ./mapping.yaml
          compliance_assessment: EXAMPLE-1
          controls:
            mfa: AC-MFA
        """;

    [Fact]
    public async Task Example_config_is_valid()
    {
        var config = (await ConfigLoader.LoadAsync(TestFiles.ExampleConfigPath, Known, TestContext.Current.CancellationToken)).Value();

        config.CisoAssistant.Token.ToString().ShouldBe("env:HOUND_CA_TOKEN");
        config.CisoAssistant.Transport.ShouldBe("rest");
        config.Collectors.ShouldHaveSingleItem().Type.ShouldBe("fake");
        config.Target.ComplianceAssessmentRefId.ShouldBe("EXAMPLE-BASELINE-2026");
        File.Exists(config.Target.MappingPath).ShouldBeTrue();
    }

    [Fact]
    public void Valid_config_is_parsed_with_paths_resolved_against_the_config_folder()
    {
        var config = ConfigLoader.Parse(Valid, Source, Known).Value();

        config.CisoAssistant.Url.ShouldBe(new Uri("http://localhost:8000/api/"));
        config.Evidence.ShouldBe(new EvidenceConfig(Path.Combine(Path.GetTempPath(), "deploy", "evidence"), 30));
        config.Target.MappingPath.ShouldBe(Path.Combine(Path.GetTempPath(), "deploy", "mapping.yaml"));
        var collector = config.Collectors.ShouldHaveSingleItem();
        collector.Name.ShouldBe("idp");
        collector.Schedule.ShouldBe("0 2 * * *");
        collector.Settings["tenant"].ShouldBe("example.com");
        collector.Secrets["api_token"].Scheme.ShouldBe(SecretScheme.File);
        collector.Secrets["api_token"].Name.ShouldBe("/run/secrets/idp_token");
    }

    [Theory]
    // Low-entropy stand-ins: the rules key on the setting name and on long unbroken letter-and-digit runs, not on randomness.
    [InlineData("token: env:HOUND_CA_TOKEN", "token: hunter2")]
    [InlineData("api_token: file:/run/secrets/idp_token", "api_token: hunter2")]
    [InlineData("tenant: example.com", "client_secret: hunter2")]
    [InlineData("tenant: example.com", "tenant: abcd1234abcd1234abcd1234abcd1234abcd1234")]
    public void Inline_secret_value_is_rejected(string valid, string inline)
    {
        var problem = ConfigLoader.Parse(Valid.Replace(valid, inline, StringComparison.Ordinal), Source, Known).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("config.secret_inline");
        problem.Message.ShouldNotContain("hunter2");
        problem.Message.ShouldNotContain("abcd1234");
    }

    [Fact]
    public void Unknown_collector_type_lists_known_types()
    {
        var problem = ConfigLoader.Parse(Valid.Replace("type: fake", "type: idp", StringComparison.Ordinal), Source, Known).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("config.collector_type_unknown");
        problem.Message.ShouldContain("fake, manual");
    }

    [Fact]
    public void All_errors_reported_together()
    {
        var yaml = Valid
            .Replace("url: http://localhost:8000/api/", "url: not a url", StringComparison.Ordinal)
            .Replace("type: fake", "type: idp", StringComparison.Ordinal)
            .Replace("\"0 2 * * *\"", "daily", StringComparison.Ordinal)
            .Replace("compliance_assessment: EXAMPLE-1", "colour: blue", StringComparison.Ordinal);

        var codes = ConfigLoader.Parse(yaml, Source, Known).Problems().Select(p => p.Code).Order(StringComparer.Ordinal).ToArray();

        codes.ShouldBe(["config.collector_type_unknown", "config.key_missing", "config.key_unknown", "config.schedule_invalid", "config.url_invalid"]);
    }

    [Fact]
    public void Problems_name_the_file_and_line()
    {
        var problem = ConfigLoader.Parse(Valid.Replace("type: fake", "type: idp", StringComparison.Ordinal), Source, Known).Problems().ShouldHaveSingleItem();

        problem.Message.ShouldStartWith($"{Source} line 9: ");
    }

    [Fact]
    public void Collector_names_must_be_unique()
    {
        var yaml = Valid.Replace("collectors:\n", "collectors:\n  - {name: idp, type: fake, schedule: \"0 3 * * *\"}\n", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Problems().ShouldHaveSingleItem().Code.ShouldBe("config.collector_name_duplicate");
    }

    [Theory]
    [InlineData("transport: rest", "rest")]
    [InlineData("transport: kafka\n  kafka_bootstrap_servers: localhost:19092", "kafka")]
    public void Transport_is_read(string line, string expected)
    {
        var yaml = Valid.Replace("token: env:HOUND_CA_TOKEN", "token: env:HOUND_CA_TOKEN\n  " + line, StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Value().CisoAssistant.Transport.ShouldBe(expected);
    }

    [Theory]
    [InlineData("transport: carrier-pigeon", "config.transport_unknown")]
    [InlineData("transport: kafka", "config.kafka_servers_missing")]
    public void Bad_transport_is_rejected(string line, string code)
    {
        var yaml = Valid.Replace("token: env:HOUND_CA_TOKEN", "token: env:HOUND_CA_TOKEN\n  " + line, StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Problems().ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("", "config.document_empty")]
    [InlineData("- a list", "config.document_not_a_map")]
    [InlineData("collectors: [", "config.yaml_invalid")]
    public void Unreadable_documents_are_rejected(string yaml, string code)
    {
        ConfigLoader.Parse(yaml, Source, Known).Problems().ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Fact]
    public void Retention_must_be_a_positive_whole_number()
    {
        var yaml = Valid.Replace("retention_days: 30", "retention_days: -1", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Problems().ShouldHaveSingleItem().Code.ShouldBe("config.retention_invalid");
    }

    [Fact]
    public async Task Missing_file_is_a_problem()
    {
        var result = await ConfigLoader.LoadAsync("no-such-config.yaml", Known, TestContext.Current.CancellationToken);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("config.file_not_found");
    }
}
