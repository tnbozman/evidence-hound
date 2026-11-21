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

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
