namespace EvidenceHound.Cli.Tests;

public sealed class ServeCommandTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("hound-serve-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _dir.Delete(recursive: true);
    }

    [Fact]
    public async Task Serve_with_a_bad_config_reports_it_and_exits()
    {
        var exitCode = await HoundCli.RunAsync(["serve", "--config", Path.Combine(_dir.FullName, "absent.yaml")], _output, _error,
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("Config file not found");
    }

    [Fact]
    public async Task Serve_lists_each_collector_with_its_next_run_then_stops_when_cancelled()
    {
        var config = Path.Combine(_dir.FullName, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: evidence
            scheduler:
              time_zone: Australia/Sydney
              jitter_seconds: 0
            collectors:
              - name: example-fake
                type: fake
                schedule: "0 3 * * *"
                settings: {}
            target:
              mapping: mapping.yaml
              compliance_assessment: EXAMPLE-BASELINE-2026
            """, TestContext.Current.CancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(2));

        var exitCode = await HoundCli.RunAsync(["serve", "--config", config], _output, _error, cancellationToken: stop.Token);

        exitCode.ShouldBe(ExitCodes.Ok);
        _error.ToString().ShouldContain("example-fake: next run");
        _error.ToString().ShouldContain("Australia/Sydney");
    }
}
