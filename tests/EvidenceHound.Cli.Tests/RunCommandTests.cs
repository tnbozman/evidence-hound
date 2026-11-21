using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

public sealed class RunCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-run-tests-").FullName;
    private readonly WireMockServer _ca = WireMockServer.Start();

    [Fact]
    public async Task Dry_run_plans_the_fake_collectors_updates_without_calling_ciso_assistant()
    {
        var exitCode = await Run(Settings(), "run", "--config", await ConfigAsync(), "--dry-run");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _ca.LogEntries.ShouldBeEmpty();
        var collector = Summary()["collectors"]![0]!;
        collector["name"]!.GetValue<string>().ShouldBe("example-fake");
        collector["outcome"]!.GetValue<string>().ShouldBe("succeeded");
        collector["publish"]!["planned"]!.GetValue<int>().ShouldBe(8);
        Directory.GetDirectories(Path.Combine(_workDir, "evidence", "example-fake")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_publish_needs_no_token()
    {
        var exitCode = await Run([], "run", "--config", await ConfigAsync(token: "env:HOUND_TEST_NOT_SET"), "--no-publish");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        Summary()["collectors"]![0]!["publish"].ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_only_exits_two()
    {
        var exitCode = await Run(Settings(), "run", "--config", await ConfigAsync(), "--only", "no-such-collector");

        exitCode.ShouldBe(ExitCodes.Usage);
        _error.ToString().ShouldContain("example-fake");
    }

    [Fact]
    public async Task Publishing_without_the_token_exits_three()
    {
        var exitCode = await Run(Settings(), "run", "--config", await ConfigAsync(token: "env:HOUND_TEST_NOT_SET"));

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("env:HOUND_TEST_NOT_SET");
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_collector_whose_publish_fails_exits_one()
    {
        _ca.Given(Request.Create().UsingAnyMethod()).RespondWith(Response.Create().WithStatusCode(400).WithBody("""{"detail": "refused"}"""));

        var exitCode = await Run(Settings(), "run", "--config", await ConfigAsync());

        exitCode.ShouldBe(ExitCodes.CollectorFailed);
        Summary()["collectors"]![0]!["outcome"]!.GetValue<string>().ShouldBe("failed");
        _ca.LogEntries.ShouldNotBeEmpty();
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.StartsWith("example-fake: ", StringComparison.Ordinal));
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["HOUND_CA_URL"] = _ca.Url + "/api/",
        ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests",
    };

    private async Task<string> ConfigAsync(string token = "env:HOUND_CA_TOKEN")
    {
        var example = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"), TestContext.Current.CancellationToken);
        var yaml = example
            .Replace("root: ../.dev/evidence", $"root: {Path.Combine(_workDir, "evidence")}", StringComparison.Ordinal)
            .Replace("mapping: ../mappings/example.yaml", $"mapping: {Path.Combine(AppContext.BaseDirectory, "files", "mappings", "example.yaml")}", StringComparison.Ordinal)
            .Replace("token: env:HOUND_CA_TOKEN", $"token: {token}", StringComparison.Ordinal);
        var path = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(path, yaml, TestContext.Current.CancellationToken);
        return path;
    }

    private Task<int> Run(Dictionary<string, string?> settings, params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), settings, TestContext.Current.CancellationToken);

    private JsonNode Summary() => JsonNode.Parse(_output.ToString())!;

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
