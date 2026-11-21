using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound framework snapshot</c> and <c>hound mapping validate --framework</c>.</summary>
public sealed class FrameworkCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-framework-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string Library => Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-repeated-library.yaml");

    [Fact]
    public async Task Snapshot_writes_the_snapshot_file()
    {
        var output = Path.Combine(_workDir, "example-repeated.json");

        var exitCode = await Run("framework", "snapshot", Library, "--out", output);

        exitCode.ShouldBe(ExitCodes.Ok);
        var json = await File.ReadAllTextAsync(output, _ct);
        json.ShouldContain("\"framework_urn\": \"urn:example:risk:framework:example-repeated\"");
        _output.ToString().ShouldContain("4 assessable requirements");
    }

    [Fact]
    public async Task Snapshot_without_out_prints_the_snapshot()
    {
        var exitCode = await Run("framework", "snapshot", Library);

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ShouldContain("\"library_urn\": \"urn:example:risk:library:example-repeated\"");
    }

    [Fact]
    public async Task Mapping_validate_with_framework_reports_ambiguous_refs()
    {
        var snapshot = Path.Combine(_workDir, "snapshot.json");
        (await Run("framework", "snapshot", Library, "--out", snapshot)).ShouldBe(ExitCodes.Ok);
        var mapping = await Write("mapping.yaml", """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-01
                rule: all_pass
                checks: [identity.mfa.admins]
            """);

        var exitCode = await Run("mapping", "validate", mapping, "--framework", snapshot);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("matches 2 rows");
        _error.ToString().ShouldContain("urn:example:risk:req_node:example-repeated:idn:act-01");
    }

    [Fact]
    public async Task Mapping_validate_with_framework_passes_a_valid_mapping()
    {
        var snapshot = Path.Combine(_workDir, "snapshot.json");
        (await Run("framework", "snapshot", Library, "--out", snapshot)).ShouldBe(ExitCodes.Ok);
        var mapping = await Write("mapping.yaml", """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-01
                rows: all
                rule: all_pass
                checks: [identity.mfa.admins]
            """);

        var exitCode = await Run("mapping", "validate", mapping, "--framework", snapshot);

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ShouldContain("valid: 1 requirements");
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

    private async Task<string> Write(string name, string content)
    {
        var path = Path.Combine(_workDir, name);
        await File.WriteAllTextAsync(path, content, _ct);
        return path;
    }
}
