using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

public sealed class CliTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-cli-tests-").FullName;

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs));

    private Task<int> RunWith(Command extra, CancellationToken cancellationToken, params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), [extra], cancellationToken);

    [Fact]
    public async Task Version_prints_semantic_version()
    {
        var exitCode = await Run("--version");

        exitCode.ShouldBe(0);
        _output.ToString().Trim().ShouldMatch(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$");
    }

    [Fact]
    public async Task Collect_dry_run_with_empty_config_exits_zero()
    {
        var config = Path.Combine(_workDir, "empty.yaml");
        await File.WriteAllTextAsync(config, ConfigWithoutCollectors, TestContext.Current.CancellationToken);

        var exitCode = await Run("collect", "--dry-run", "--config", config);

        exitCode.ShouldBe(0);
        _logs.Messages.ShouldContain("0 collectors configured");
    }

    [Fact]
    public async Task Unknown_option_exits_two()
    {
        var exitCode = await Run("collect", "--no-such-option");

        exitCode.ShouldBe(2);
        _error.ToString().ShouldContain("--no-such-option");
        _error.ToString().ShouldContain("--help");
    }

    [Fact]
    public async Task Collect_with_missing_config_file_exits_three()
    {
        var exitCode = await Run("collect", "--dry-run", "--config", Path.Combine(_workDir, "missing.yaml"));

        exitCode.ShouldBe(3);
        _logs.Messages.ShouldContain(m => m.Contains("missing.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_stores_each_collectors_evidence_with_a_manifest()
    {
        var config = Path.Combine(_workDir, "fake.yaml");
        await File.WriteAllTextAsync(config, ConfigWithoutCollectors.Replace(
            "collectors: []", "collectors:\n  - {name: example-fake, type: fake, schedule: \"0 2 * * *\", settings: {tenant: example.com}}", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var exitCode = await Run("collect", "--config", config);

        exitCode.ShouldBe(0);
        var run = Directory.GetDirectories(Path.Combine(_workDir, "evidence", "example-fake")).ShouldHaveSingleItem();
        File.Exists(Path.Combine(run, "_manifest.json")).ShouldBeTrue();
        Directory.GetFiles(run).Length.ShouldBe(5);
        _logs.Messages.ShouldContain(m => m.Contains("example-fake", StringComparison.Ordinal) && m.Contains("4 files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_with_a_missing_secret_exits_one_and_names_the_reference()
    {
        var config = Path.Combine(_workDir, "fake.yaml");
        await File.WriteAllTextAsync(config, ConfigWithoutCollectors.Replace(
            "collectors: []", "collectors:\n  - {name: example-fake, type: fake, schedule: \"0 2 * * *\", secrets: {api_token: \"env:HOUND_TEST_NOT_SET\"}}", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var exitCode = await Run("collect", "--config", config);

        exitCode.ShouldBe(1);
        _error.ToString().ShouldContain("env:HOUND_TEST_NOT_SET");
    }

    [Fact]
    public async Task Mapping_validate_prints_requirement_count()
    {
        var mapping = Path.Combine(_workDir, "mapping.yaml");
        await File.WriteAllTextAsync(mapping, """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: all_pass
                checks: [identity.mfa.admins]
              - ref: "1.2"
                rule: any_pass
                checks: [backup.restore_tested]
            """, TestContext.Current.CancellationToken);

        var exitCode = await Run("mapping", "validate", mapping);

        exitCode.ShouldBe(0);
        _output.ToString().Trim().ShouldBe("valid: 2 requirements");
    }

    [Fact]
    public async Task Mapping_validate_reports_problems_and_exits_three()
    {
        var mapping = Path.Combine(_workDir, "bad.yaml");
        await File.WriteAllTextAsync(mapping, """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: most_pass
                checks: [identity.mfa.admins]
            """, TestContext.Current.CancellationToken);

        var exitCode = await Run("mapping", "validate", mapping);

        exitCode.ShouldBe(3);
        _error.ToString().ShouldContain("line 4");
        _error.ToString().ShouldContain("most_pass");
    }

    [Fact]
    public async Task Mapping_validate_with_missing_file_exits_three()
    {
        var exitCode = await Run("mapping", "validate", Path.Combine(_workDir, "missing.yaml"));

        exitCode.ShouldBe(3);
        _error.ToString().ShouldContain("missing.yaml");
    }

    [Fact]
    public async Task Mapping_apply_prints_a_row_per_requirement()
    {
        var exitCode = await Run("mapping", "apply", RepoFile("mappings", "example.yaml"), RepoFile("fixtures", "checks-sample.json"));

        exitCode.ShouldBe(0);
        var lines = _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0].ShouldStartWith("REQUIREMENT");
        lines.Skip(1).Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2))).ShouldBe([
            "EX-1.1 non_compliant",
            "EX-1.2 partially_compliant",
            "EX-2.1 compliant",
            "EX-3.1 not_assessed",
            "EX-4.1 not_assessed",
        ]);
    }

    [Fact]
    public async Task Mapping_apply_with_bad_checks_file_exits_three()
    {
        var checks = Path.Combine(_workDir, "checks.json");
        await File.WriteAllTextAsync(checks, "{", TestContext.Current.CancellationToken);

        var exitCode = await Run("mapping", "apply", RepoFile("mappings", "example.yaml"), checks);

        exitCode.ShouldBe(3);
        _error.ToString().ShouldContain("checks.json");
    }

    [Fact]
    public async Task Plan_dry_run_prints_dispatcher_messages()
    {
        var exitCode = await Run("plan", "--dry-run", "--config", RepoFile("examples", "example-pty-ltd.yaml"), "--checks", RepoFile("fixtures", "checks-sample.json"));

        exitCode.ShouldBe(0);
        var messages = System.Text.Json.Nodes.JsonNode.Parse(_output.ToString())!.AsArray();
        messages.Select(m => m!["message_type"]!.GetValue<string>()).ShouldBe([
            .. Enumerable.Repeat("upload_attachment", 5),
            .. Enumerable.Repeat("update_requirement_assessment", 5),
            .. Enumerable.Repeat("update_applied_control", 2),
        ]);
        messages[5]!["selector"]!["compliance_assessment__ref_id"]!.GetValue<string>().ShouldBe("EXAMPLE-BASELINE-2026");
        messages[5]!["values"]!["result"]!.GetValue<string>().ShouldBe("non_compliant");
        messages[10]!["selector"]!["ref_id"]!.GetValue<string>().ShouldBe("AC-BACKUP");
        messages[0]!["values"]!["file_content"]!.GetValue<string>().ShouldContain("dry run");
    }

    [Fact]
    public async Task Plan_without_dry_run_exits_two()
    {
        var exitCode = await Run("plan", "--config", RepoFile("examples", "example-pty-ltd.yaml"), "--checks", RepoFile("fixtures", "checks-sample.json"));

        exitCode.ShouldBe(2);
        _error.ToString().ShouldContain("--dry-run");
    }

    [Fact]
    public async Task Plan_with_config_without_target_exits_three()
    {
        var config = Path.Combine(_workDir, "no-target.yaml");
        await File.WriteAllTextAsync(config, "collectors: []\n", TestContext.Current.CancellationToken);

        var exitCode = await Run("plan", "--dry-run", "--config", config, "--checks", RepoFile("fixtures", "checks-sample.json"));

        exitCode.ShouldBe(3);
        _error.ToString().ShouldContain("target");
    }

    [Theory]
    [InlineData("missing-config.yaml", "checks-sample.json")]
    [InlineData("example-pty-ltd.yaml", "missing-checks.json")]
    public async Task Plan_with_a_missing_file_exits_three(string config, string checks)
    {
        var configPath = config.StartsWith("missing", StringComparison.Ordinal) ? Path.Combine(_workDir, config) : RepoFile("examples", config);
        var checksPath = checks.StartsWith("missing", StringComparison.Ordinal) ? Path.Combine(_workDir, checks) : RepoFile("fixtures", checks);

        var exitCode = await Run("plan", "--dry-run", "--config", configPath, "--checks", checksPath);

        exitCode.ShouldBe(3);
        _error.ToString().ShouldContain("missing-");
    }

    [Fact]
    public async Task Unexpected_exception_is_logged_as_critical_and_exits_one()
    {
        var boom = new Command("boom");
        boom.SetAction(int (_) => throw new InvalidOperationException("kaboom"));

        var exitCode = await RunWith(boom, TestContext.Current.CancellationToken, "boom");

        exitCode.ShouldBe(1);
        var critical = _logs.Entries.Single(e => e.Level == LogLevel.Critical);
        critical.Exception.ShouldBeOfType<InvalidOperationException>();
        critical.Message.ShouldContain("'boom'");
        _error.ToString().ShouldContain("crit: Unexpected failure running 'boom'");
        _error.ToString().ShouldContain("InvalidOperationException: kaboom");
        _error.ToString().ShouldNotContain(" at ");
    }

    [Fact]
    public async Task Cancellation_exits_130()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = new Command("wait");
        wait.SetAction(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });

        var exitCode = await RunWith(wait, cancellation.Token, "wait");

        exitCode.ShouldBe(130);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Problems_are_logged_as_errors_with_their_code()
    {
        var mapping = Path.Combine(_workDir, "bad.yaml");
        await File.WriteAllTextAsync(mapping, "framework_urn: urn:example:x\nrequirements: {}\n", TestContext.Current.CancellationToken);

        var exitCode = await Run("mapping", "validate", mapping);

        exitCode.ShouldBe(3);
        var problem = _logs.Entries.Single(e => e.Level == LogLevel.Error);
        problem.State.Select(kv => (kv.Key, kv.Value)).ShouldBe([
            ("Code", (object?)"mapping.requirements_not_a_list"),
            ("Message", problem.Message),
            ("{OriginalFormat}", "{Message}"),
        ]);
        problem.State[0].Key.ShouldBe("Code");
        ((System.Collections.IEnumerable)problem.State).Cast<object>().Count().ShouldBe(3);
        _error.ToString().ShouldBe($"error: {problem.Message}{Environment.NewLine}");
    }

    [Fact]
    public async Task Collect_with_invalid_yaml_exits_three_with_the_line()
    {
        var config = Path.Combine(_workDir, "broken.yaml");
        await File.WriteAllTextAsync(config, "collectors: [\n", TestContext.Current.CancellationToken);

        var exitCode = await Run("collect", "--dry-run", "--config", config);

        exitCode.ShouldBe(3);
        _error.ToString().ShouldStartWith($"error: {config} line 2: not valid YAML");
    }

    private static readonly string ConfigWithoutCollectors = """
        ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
        evidence: {root: ./evidence}
        collectors: []
        target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
        """;

    private static string RepoFile(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "files", folder, name);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
