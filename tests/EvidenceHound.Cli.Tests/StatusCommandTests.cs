using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Cli.Tests;

public sealed class StatusCommandTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-status-tests-").FullName;

    [Fact]
    public async Task Shows_latest_run_per_collector()
    {
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
              - {name: example-second, type: fake, schedule: "0 3 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);
        var store = new FileRunRecordStore(Path.Combine(_workDir, "evidence"));
        await store.SaveAsync(new RunRecord("example-fake", Start.AddDays(-1), Start.AddDays(-1).AddSeconds(3), CollectorOutcome.Failed, 0, 0, 0, [new("x", "y")], null), TestContext.Current.CancellationToken);
        await store.SaveAsync(new RunRecord("example-fake", Start, Start.AddSeconds(4), CollectorOutcome.Succeeded, 3, 1, 0, [], new Runtime.Status.PublishCounts(0, 6, 2, 0)), TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(["status", "--config", config], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3);
        lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["COLLECTOR", "LAST", "RUN", "(UTC)", "OUTCOME", "PASSED", "FAILED", "PUBLISHED"]);
        lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["example-fake", "2026-06-30", "02:00:04", "succeeded", "3", "1", "6", "sent,", "2", "skipped,", "0", "failed"]);
        lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["example-second", "never", "run", "-", "-", "-", "-"]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
