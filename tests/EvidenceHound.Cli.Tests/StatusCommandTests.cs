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

    [Fact]
    public async Task Shows_changed_unchanged_and_drift_per_audit_from_the_history()
    {
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
            targets:
              - {mapping: ./mapping.yaml, compliance_assessment: EX-E8-2026}
              - {mapping: ./other.yaml, compliance_assessment: EX-ESSENTIALS-2026}
            """, TestContext.Current.CancellationToken);
        var history = new FileRunHistory(Path.Combine(_workDir, "evidence"));
        await history.AppendAsync("EX-E8-2026", new HistoryEntry(Start.AddDays(-1), "20260629T020000Z", 16, 0, 0), TestContext.Current.CancellationToken);
        await history.AppendAsync("EX-E8-2026", new HistoryEntry(Start, "20260630T020000Z", 1, 15, 1), TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(["status", "--config", config], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var header = Array.FindIndex(lines, l => l.StartsWith("AUDIT", StringComparison.Ordinal));
        header.ShouldBeGreaterThan(0);
        lines[header].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["AUDIT", "LAST", "RUN", "(UTC)", "RUN", "ID", "CHANGED", "UNCHANGED", "DRIFT"]);
        lines[header + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["EX-E8-2026", "2026-06-30", "02:00:00", "20260630T020000Z", "1", "15", "1"]);
        lines[header + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["EX-ESSENTIALS-2026", "never", "run", "-", "-", "-", "-"]);
    }

    [Fact]
    public async Task A_series_target_shows_the_period_open_today()
    {
        // s09-49: hound run publishes a series target (EX-E8) to the period open that day, and status shows that period's run.
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            scope: scope
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EX-E8}
            """, TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(_workDir, "scope", "audits"));
        await File.WriteAllTextAsync(Path.Combine(_workDir, "scope", "setup.yaml"), "organisation:\n  root_folder: Global\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "scope", "audits", "ex-e8.yaml"), """
            ref_id: EX-E8
            name: Essential Eight ML2
            framework: urn:intuitem:risk:framework:essential-eight-2023
            domain: Example Pty Ltd
            perimeter: Corporate IT
            cycle: {period: quarter, starts: "2026-07-01"}
            """, TestContext.Current.CancellationToken);
        var history = new FileRunHistory(Path.Combine(_workDir, "evidence"));
        await history.AppendAsync("EX-E8-Q2-2027FY", new HistoryEntry(new DateTimeOffset(2026, 11, 23, 4, 0, 0, TimeSpan.Zero), "20261123T040000Z", 0, 3, 0), TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(
            ["status", "--config", config], TextReader.Null, _output, _error, new Dictionary<string, string?> { ["HOUND_TODAY"] = "2026-11-23" }, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var header = Array.FindIndex(lines, l => l.StartsWith("AUDIT", StringComparison.Ordinal));
        lines[header + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["EX-E8-Q2-2027FY", "2026-11-23", "04:00:00", "20261123T040000Z", "0", "3", "0"]);
    }

    [Fact]
    public async Task A_missing_config_exits_three()
    {
        var exitCode = await HoundCli.RunAsync(
            ["status", "--config", Path.Combine(_workDir, "missing.yaml")], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("missing.yaml");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
