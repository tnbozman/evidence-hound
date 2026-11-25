using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s15-01: <c>hound library validate</c> and <c>hound library snapshot</c> for custom risk matrices and threat catalogues.</summary>
public sealed class LibraryCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-library-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string Library => Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-risk-library.yaml");

    [Fact]
    public async Task Validate_passes_a_good_library_and_says_what_it_holds()
    {
        var exitCode = await Run("library", "validate", Library);

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ShouldContain("valid: 1 risk matrix, 2 threats");
    }

    [Fact]
    public async Task Validate_reports_a_grid_cell_that_is_not_a_risk_level()
    {
        var broken = Path.Combine(_workDir, "broken.yaml");
        await File.WriteAllTextAsync(broken, (await File.ReadAllTextAsync(Library, _ct)).Replace("- [1, 2, 2, 2]", "- [1, 2, 2, 7]", StringComparison.Ordinal), _ct);

        var exitCode = await Run("library", "validate", broken);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("probability 'Likely' and impact 'Severe' is 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Snapshot_writes_level_names_and_threats()
    {
        var output = Path.Combine(_workDir, "example-risk.json");

        var exitCode = await Run("library", "snapshot", Library, "--out", output);

        exitCode.ShouldBe(ExitCodes.Ok);
        var json = await File.ReadAllTextAsync(output, _ct);
        json.ShouldContain("\"Likely\"");
        json.ShouldContain("\"THR-02\"");
        _output.ToString().ShouldContain("1 risk matrix, 2 threats");
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
}
