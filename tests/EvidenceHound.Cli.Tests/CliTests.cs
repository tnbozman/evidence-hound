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
        await File.WriteAllTextAsync(config, "collectors: []\n", TestContext.Current.CancellationToken);

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
    public async Task Collect_without_dry_run_exits_two_until_collectors_exist()
    {
        var config = Path.Combine(_workDir, "empty.yaml");
        await File.WriteAllTextAsync(config, "collectors: []\n", TestContext.Current.CancellationToken);

        var exitCode = await Run("collect", "--config", config);

        exitCode.ShouldBe(2);
        _error.ToString().ShouldContain("--dry-run");
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
            .. Enumerable.Repeat("update_requirement_assessment", 4),
            .. Enumerable.Repeat("update_applied_control", 2),
        ]);
        messages[5]!["selector"]!["compliance_assessment__ref_id"]!.GetValue<string>().ShouldBe("EXAMPLE-BASELINE-2026");
        messages[5]!["values"]!["result"]!.GetValue<string>().ShouldBe("non_compliant");
        messages[9]!["selector"]!["ref_id"]!.GetValue<string>().ShouldBe("AC-BACKUP");
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

    private static string RepoFile(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "files", folder, name);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
