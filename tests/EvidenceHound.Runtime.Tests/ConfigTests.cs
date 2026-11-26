using EvidenceHound.Collectors;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Tests;

public class ConfigTests
{
    private static readonly Dictionary<string, CollectorDescription> Known = new(StringComparer.Ordinal)
    {
        ["fake"] = new([], [], []),
        ["manual"] = new([], [], []),
    };

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
        config.Collectors.ShouldHaveSingleItem().Type.ShouldBe("fake");
        config.Targets[0].ComplianceAssessmentRefId.ShouldBe("EXAMPLE-BASELINE-2026");
        config.Targets[0].HealthControlRefId.ShouldBe("AC-AUTO-EVIDENCE");
        File.Exists(config.Targets[0].MappingPath).ShouldBeTrue();
    }

    [Fact]
    public void Scheduler_defaults_to_utc_with_five_minutes_of_jitter()
    {
        var config = ConfigLoader.Parse(Valid, Source, Known).Value();

        config.Scheduler.TimeZone.ShouldBe(TimeZoneInfo.Utc);
        config.Scheduler.MaxJitter.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Scheduler_time_zone_and_jitter_are_read()
    {
        var config = ConfigLoader.Parse(Valid + "\nscheduler:\n  time_zone: Australia/Sydney\n  jitter_seconds: 0\n", Source, Known).Value();

        config.Scheduler.TimeZone.Id.ShouldBe("Australia/Sydney");
        config.Scheduler.MaxJitter.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("  time_zone: Mars/Olympus_Mons", "config.time_zone_unknown")]
    [InlineData("  jitter_seconds: -1", "config.jitter_invalid")]
    [InlineData("  every: 5", "config.key_unknown")]
    public void Scheduler_problems_are_reported_with_line(string line, string code)
    {
        var problem = ConfigLoader.Parse(Valid + "\nscheduler:\n" + line + "\n", Source, Known).Match(_ => throw new InvalidOperationException("parsed"), p => p[0]);

        problem.Code.ShouldBe(code);
        problem.Message.ShouldContain($"line {Valid.Split('\n').Length + 2}:");
    }

    [Fact]
    public void Single_target_is_read_as_one_item_list()
    {
        var config = ConfigLoader.Parse(Valid, Source, Known).Value();

        var target = config.Targets.ShouldHaveSingleItem();
        target.ComplianceAssessmentRefId.ShouldBe("EXAMPLE-1");
        target.Controls["mfa"].ShouldBe("AC-MFA");
    }

    [Fact]
    public void Targets_list_is_read_in_order_with_paths_resolved()
    {
        var yaml = Valid[..Valid.IndexOf("target:", StringComparison.Ordinal)] + """
            targets:
              - mapping: ./essential-eight-2023.yaml
                compliance_assessment: EX-E8-2026
                controls:
                  mfa: AC-MFA
                health_control: AC-AUTO-EVIDENCE
              - mapping: ./asd-essentials.yaml
                compliance_assessment: EX-ESSENTIALS-2026
            """;

        var config = ConfigLoader.Parse(yaml, Source, Known).Value();

        config.Targets.Select(t => (t.ComplianceAssessmentRefId, Path.GetFileName(t.MappingPath))).ShouldBe([
            ("EX-E8-2026", "essential-eight-2023.yaml"),
            ("EX-ESSENTIALS-2026", "asd-essentials.yaml"),
        ]);
        config.Targets[1].Controls.ShouldBeEmpty();
        config.Targets[0].MappingPath.ShouldBe(Path.Combine(Path.GetTempPath(), "deploy", "essential-eight-2023.yaml"));
    }

    [Fact]
    public void Target_and_targets_together_is_a_problem()
    {
        var yaml = Valid + """

            targets:
              - mapping: ./other.yaml
                compliance_assessment: EX-OTHER
            """;

        ConfigLoader.Parse(yaml, Source, Known).Problems().Select(p => p.Code).ShouldBe(["config.targets_conflict"]);
    }

    [Fact]
    public void An_empty_target_list_means_no_audit_yet()
    {
        // s18-08: hound init writes no organisation audit by default; each audit approved later adds its target.
        var yaml = Valid[..Valid.IndexOf("target:", StringComparison.Ordinal)] + "targets: []\n";

        ConfigLoader.Parse(yaml, Source, Known).Value().Targets.ShouldBeEmpty();
    }

    [Fact]
    public void Two_targets_for_one_audit_is_a_problem()
    {
        var yaml = Valid[..Valid.IndexOf("target:", StringComparison.Ordinal)] + """
            targets:
              - mapping: ./a.yaml
                compliance_assessment: EX-1
              - mapping: ./b.yaml
                compliance_assessment: EX-1
            """;

        ConfigLoader.Parse(yaml, Source, Known).Problems().Select(p => p.Code).ShouldBe(["config.target_duplicate"]);
    }

    [Fact]
    public void Valid_config_is_parsed_with_paths_resolved_against_the_config_folder()
    {
        var config = ConfigLoader.Parse(Valid, Source, Known).Value();

        config.CisoAssistant.Url.ShouldBe(new Uri("http://localhost:8000/api/"));
        config.Evidence.ShouldBe(new EvidenceConfig(Path.Combine(Path.GetTempPath(), "deploy", "evidence"), 30));
        config.Targets[0].MappingPath.ShouldBe(Path.Combine(Path.GetTempPath(), "deploy", "mapping.yaml"));
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

    [Theory]
    // An address or path setting is judged by its value: a profile's token_url names where to sign in, not a token.
    [InlineData("token_url: https://login.example.com/oauth2/token")]
    [InlineData("secrets_path: /config/secrets-list")]
    public void Address_named_after_a_secret_is_not_a_secret(string setting)
    {
        var collector = ConfigLoader.Parse(Valid.Replace("tenant: example.com", setting, StringComparison.Ordinal), Source, Known).Value().Collectors[0];

        collector.Settings.Count.ShouldBe(1);
    }

    [Theory]
    // A folder or path setting holds a path: a GUID-named folder (a SharePoint library sync, a tenant ID) is not a token.
    [InlineData("drop_folder: /evidence/manual/3f2a9c1e-7b4d-4e8a-9c2f-1d5e6a7b8c9d")]
    [InlineData("drop_folder: ./manual/3f2a9c1e7b4d4e8a9c2f1d5e6a7b8c9d")]
    [InlineData("profiles_path: ../profiles/3f2a9c1e7b4d4e8a9c2f1d5e6a7b8c9d")]
    [InlineData("export_dir: /data/3f2a9c1e7b4d4e8a9c2f1d5e6a7b8c9d")]
    public void Path_setting_with_a_long_folder_name_is_not_a_secret(string setting)
    {
        var collector = ConfigLoader.Parse(Valid.Replace("tenant: example.com", setting, StringComparison.Ordinal), Source, Known).Value().Collectors[0];

        collector.Settings.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("drop_folder: abcd1234abcd1234abcd1234abcd1234abcd1234")]
    [InlineData("drop_folder: '-----BEGIN PRIVATE KEY-----'")]
    public void Path_setting_that_is_not_a_path_is_still_checked(string setting) =>
        ConfigLoader.Parse(Valid.Replace("tenant: example.com", setting, StringComparison.Ordinal), Source, Known).Problems()
            .ShouldHaveSingleItem().Code.ShouldBe("config.secret_inline");

    [Theory]
    [InlineData("token_url: https://login.example.com/oauth2/token?key=abcd1234abcd1234abcd1234abcd1234abcd1234")]
    [InlineData("token_url: hunter2")]
    public void Address_setting_holding_a_secret_is_rejected(string setting) =>
        ConfigLoader.Parse(Valid.Replace("tenant: example.com", setting, StringComparison.Ordinal), Source, Known).Problems()
            .ShouldHaveSingleItem().Code.ShouldBe("config.secret_inline");

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

    [Fact]
    public void Transport_rest_is_still_accepted()
    {
        var yaml = Valid.Replace("token: env:HOUND_CA_TOKEN", "token: env:HOUND_CA_TOKEN\n  transport: rest", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Value().CisoAssistant.Url.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("transport: carrier-pigeon", "config.transport_unknown")]
    [InlineData("transport: kafka", "config.transport_removed")]
    [InlineData("transport: kafka\n  kafka_bootstrap_servers: localhost:19092", "config.transport_removed")]
    [InlineData("kafka_bootstrap_servers: localhost:19092", "config.transport_removed")]
    public void Bad_transport_is_rejected(string line, string code)
    {
        // s19-02: the Kafka transport was removed in release 3; hound publishes over the REST API only.
        var yaml = Valid.Replace("token: env:HOUND_CA_TOKEN", "token: env:HOUND_CA_TOKEN\n  " + line, StringComparison.Ordinal);

        var problems = ConfigLoader.Parse(yaml, Source, Known).Problems();

        problems.Select(p => p.Code).Distinct().ShouldHaveSingleItem().ShouldBe(code);
    }

    [Theory]
    [InlineData("", "config.document_empty")]
    [InlineData("- a list", "config.document_not_a_map")]
    [InlineData("collectors: [", "config.yaml_invalid")]
    public void Unreadable_documents_are_rejected(string yaml, string code)
    {
        ConfigLoader.Parse(yaml, Source, Known).Problems().ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("collectors:\n  - name: idp", "collectors: idp\nunused:\n  - name: idp", "config.collectors_not_a_list")]
    [InlineData("  - name: idp\n    type: fake", "  - just-a-name\n  - name: idp\n    type: fake", "config.collector_not_a_map")]
    [InlineData("    settings:\n      tenant: example.com", "    settings: [example.com]", "config.section_not_a_map")]
    [InlineData("    settings:\n      tenant: example.com", "    settings:\n      tenant: [a, b]", "config.value_invalid")]
    [InlineData("    secrets:\n      api_token: file:/run/secrets/idp_token", "    secrets:\n      api_token: [a]", "config.value_invalid")]
    [InlineData("    settings:\n      tenant: example.com", "    settings:\n      cert: '-----BEGIN PRIVATE KEY-----'", "config.secret_inline")]
    [InlineData("- name: idp", "- name: Idp_1", "config.collector_name_invalid")]
    [InlineData("url: http://localhost:8000/api/", "url: ftp://localhost/api/", "config.url_invalid")]
    [InlineData("url: http://localhost:8000/api/", "url: ''", "config.value_invalid")]
    [InlineData("evidence:\n  root: ./evidence", "evidence: ./evidence\nstore:\n  root: ./evidence", "config.section_not_a_map")]
    public void Badly_shaped_values_are_rejected(string valid, string invalid, string code)
    {
        var yaml = Valid.Replace(valid, invalid, StringComparison.Ordinal);
        yaml.ShouldNotBe(Valid);

        ConfigLoader.Parse(yaml, Source, Known).Problems().Select(p => p.Code).ShouldContain(code);
    }

    [Fact]
    public void Health_control_is_optional()
    {
        ConfigLoader.Parse(Valid, Source, Known).Value().Targets[0].HealthControlRefId.ShouldBeNull();
        var yaml = Valid.Replace("  compliance_assessment: EXAMPLE-1", "  compliance_assessment: EXAMPLE-1\n  health_control: AC-AUTO", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Value().Targets[0].HealthControlRefId.ShouldBe("AC-AUTO");
    }

    [Fact]
    public void A_target_publishes_rows_with_no_evidence_as_non_compliant_unless_it_opts_out()
    {
        // s15-14: CISO Assistant leaves not-assessed rows out of its compliance percentage, so they go as non-compliant by default.
        ConfigLoader.Parse(Valid, Source, Known).Value().Targets[0].NoEvidence.ShouldBe(EvidenceHound.Core.Mapping.ComplianceResult.NonCompliant);
        var yaml = Valid.Replace("  compliance_assessment: EXAMPLE-1", "  compliance_assessment: EXAMPLE-1\n  no_evidence: not_assessed", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Value().Targets[0].NoEvidence.ShouldBe(EvidenceHound.Core.Mapping.ComplianceResult.NotAssessed);
        ConfigLoader.Parse(yaml.Replace("no_evidence: not_assessed", "no_evidence: compliant", StringComparison.Ordinal), Source, Known).Problems()
            .ShouldContain(p => p.Code == "config.no_evidence_invalid");
    }

    [Fact]
    public void Retention_is_optional()
    {
        var yaml = Valid.Replace("  retention_days: 30\n", "", StringComparison.Ordinal);

        ConfigLoader.Parse(yaml, Source, Known).Value().Evidence.RetentionDays.ShouldBeNull();
    }

    [Fact]
    public void Collector_settings_and_secrets_are_optional()
    {
        var yaml = Valid.Replace("collectors:\n", "collectors:\n  - {name: second, type: fake, schedule: \"0 3 * * *\"}\n", StringComparison.Ordinal);

        var second = ConfigLoader.Parse(yaml, Source, Known).Value().Collectors[0];

        second.Settings.ShouldBeEmpty();
        second.Secrets.ShouldBeEmpty();
    }

    [Fact]
    public void Scope_and_period_retention_are_read()
    {
        var yaml = "scope: ../compliance-repo/scope\n" + Valid.Replace("retention_days: 30", "retention_days: 30\n  period_retention_years: 10", StringComparison.Ordinal);

        var config = ConfigLoader.Parse(yaml, Source, Known).Value();

        config.Scope.ShouldBe(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "compliance-repo", "scope")));
        config.Evidence.PeriodRetentionYears.ShouldBe(10);
    }

    [Fact]
    public void Scope_is_optional_and_period_retention_defaults_to_seven_years()
    {
        var config = ConfigLoader.Parse(Valid, Source, Known).Value();

        (config.Scope, config.Evidence.PeriodRetentionYears).ShouldBe((null, 7));
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
