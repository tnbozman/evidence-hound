using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound scope validate</c> and <c>hound scope new-audit</c>.</summary>
public sealed class ScopeCommandTests : IDisposable
{
    internal const string Setup = """
        organisation:
          root_folder: Global
        frameworks:
          - urn: urn:example:risk:framework:example-baseline
            snapshot: ../frameworks/example-baseline.json
        domains:
          - name: Example Pty Ltd
            perimeter:
              name: Example baseline
            systems:
              - name: Example 365
                type: primary
        """;

    internal const string Controls = """
        controls:
          - ref_id: AC-MFA
            name: Multi-factor authentication
            domain: Example Pty Ltd
            systems: [Example 365]
        """;

    internal const string Audit = """
        ref_id: EXAMPLE-BASELINE-2026
        name: Example baseline 2026
        framework: urn:example:risk:framework:example-baseline
        groups: ["1"]
        domain: Example Pty Ltd
        perimeter: Example baseline
        systems: [Example 365]
        controls:
          - ref_id: AC-MFA
            requirements: [EX-1.1]
        unlisted:
          result: not_assessed
        """;

    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        "urn:example:risk:framework:example-baseline",
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two")],
        [
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1", "2"], "Every account uses multi-factor authentication"),
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-2.1", "EX-2.1", null, ["2"], "Patches applied within 48 hours"),
        ]);

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-scope-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ScopeDir => Path.Combine(_workDir, "scope");

    [Fact]
    public async Task Validate_passes_a_valid_scope()
    {
        await WriteScopeAsync();

        var exitCode = await Run("scope", "validate", ScopeDir);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("scope valid: 1 domain(s), 1 control(s), 1 audit(s)");
    }

    [Fact]
    public async Task Json_output_has_code_file_line_and_hint()
    {
        await WriteScopeAsync(audit: Audit.Replace("systems: [Example 365]", "systems: [Example tablets]", StringComparison.Ordinal));

        var exitCode = await Run("scope", "validate", ScopeDir, "--format", "json");

        exitCode.ShouldBe(ExitCodes.Configuration);
        var problem = JsonNode.Parse(_output.ToString())!.AsArray().ShouldHaveSingleItem()!;
        problem["code"]!.GetValue<string>().ShouldBe("scope.reference_unknown");
        problem["file"]!.GetValue<string>().ShouldBe(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"));
        problem["line"]!.GetValue<int>().ShouldBe(7);
        problem["message"]!.GetValue<string>().ShouldContain("Example tablets");
        problem["hint"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Text_output_names_file_line_and_code()
    {
        await WriteScopeAsync(audit: Audit.Replace("groups: [\"1\"]", "groups: [\"3\"]", StringComparison.Ordinal));

        var exitCode = await Run("scope", "validate", ScopeDir);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain($"{Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml")} line 4: group '3'");
        _error.ToString().ShouldContain("scope.group_unknown");
    }

    [Fact]
    public async Task Missing_setup_file_is_reported()
    {
        Directory.CreateDirectory(ScopeDir);

        var exitCode = await Run("scope", "validate", ScopeDir);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("scope.file_missing");
    }

    [Fact]
    public async Task Validate_reports_a_document_file_that_does_not_exist()
    {
        await WriteScopeAsync(Audit.Replace("controls:", "documents:\n  - id: mfa-policy\n    name: MFA policy\n    files: [documents/mfa-policy.pdf]\n    systems: [Example 365]\n    requirements: [EX-1.1]\ncontrols:", StringComparison.Ordinal));

        var exitCode = await Run("scope", "validate", ScopeDir);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("scope.document_file_missing", StringComparison.Ordinal) && m.Contains("documents/mfa-policy.pdf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_with_config_checks_that_targets_are_in_scope()
    {
        await WriteScopeAsync();
        var mapping = Path.Combine(AppContext.BaseDirectory, "files", "mappings", "example.yaml");
        var config = Path.Combine(_workDir, "hound.yaml");
        var example = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"), _ct);
        await File.WriteAllTextAsync(config, example.Replace("mapping: ../mappings/example.yaml", $"mapping: {mapping}", StringComparison.Ordinal), _ct);

        var exitCode = await Run("scope", "validate", ScopeDir, "--config", config, "--format", "json");

        exitCode.ShouldBe(ExitCodes.Configuration);
        var problems = JsonNode.Parse(_output.ToString())!.AsArray();
        problems.Select(p => p!["code"]!.GetValue<string>()).ShouldAllBe(code => code == "config.target_not_in_scope");
        problems.Select(p => p!["message"]!.GetValue<string>()).ShouldContain(m => m.Contains("AC-BACKUP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task New_audit_writes_a_starting_audit_file()
    {
        await WriteScopeAsync();
        var outFile = Path.Combine(ScopeDir, "audits", "new.yaml");

        var exitCode = await Run(
            "scope", "new-audit", "--framework", Path.Combine(_workDir, "frameworks", "example-baseline.json"), "--groups", "1,2", "--ref-id", "EX-NEW-2026", "--out", outFile);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var yaml = await File.ReadAllTextAsync(outFile, _ct);
        yaml.ShouldContain("ref_id: EX-NEW-2026");
        yaml.ShouldContain("  # Patches applied within 48 hours\n  - ref: EX-2.1\n");
        _output.ToString().ShouldContain("2 requirements");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs));

    private async Task WriteScopeAsync(string audit = Audit)
    {
        Directory.CreateDirectory(Path.Combine(ScopeDir, "audits"));
        Directory.CreateDirectory(Path.Combine(_workDir, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), Setup, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), Controls, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"), audit, _ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(Snapshot), _ct);
    }
}
