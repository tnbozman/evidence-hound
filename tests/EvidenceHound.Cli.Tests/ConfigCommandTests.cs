using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-config-tests-").FullName;

    [Fact]
    public async Task Validate_prints_valid_for_the_example_config()
    {
        var exitCode = await Run("config", "validate", Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"));

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().Trim().ShouldBe("valid");
    }

    [Fact]
    public async Task Validate_reports_every_problem_and_exits_three()
    {
        var config = Path.Combine(_workDir, "bad.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: hunter2}
            evidence: {root: ./evidence}
            collectors:
              - {name: idp, type: no-such-type, schedule: "0 2 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);

        var exitCode = await Run("config", "validate", config);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _output.ToString().ShouldBeEmpty();
        _logs.Entries.Count(e => e.Level == LogLevel.Error).ShouldBe(2);
        _error.ToString().ShouldContain("no-such-type");
        _error.ToString().ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task Check_secrets_reports_a_missing_secret_without_its_value_and_exits_three()
    {
        var exitCode = await RunWith(new Dictionary<string, string?>(), "config", "validate", "--check-secrets", ExampleConfig);

        exitCode.ShouldBe(ExitCodes.Configuration);
        var problem = _logs.Entries.ShouldHaveSingleItem();
        problem.Message.ShouldContain("env:HOUND_CA_TOKEN");
        problem.State[0].Value.ShouldBe("secret.missing");
    }

    [Fact]
    public async Task Check_secrets_prints_valid_when_every_secret_resolves()
    {
        var exitCode = await RunWith(new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = "hunter2" }, "config", "validate", "--check-secrets", ExampleConfig);

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().Trim().ShouldBe("valid");
        _error.ToString().ShouldNotContain("hunter2");
    }

    private static string ExampleConfig => Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml");

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs));

    private Task<int> RunWith(IReadOnlyDictionary<string, string?> settings, params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), settings, TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
