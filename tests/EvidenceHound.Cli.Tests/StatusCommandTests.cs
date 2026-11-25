using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
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
    public async Task A_failed_latest_run_shows_why_below_the_table()
    {
        // s15-D3: a collector whose checks all errored showed "0 0" and nothing else; its problems now follow the table.
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);
        var store = new FileRunRecordStore(Path.Combine(_workDir, "evidence"));
        await store.SaveAsync(
            new RunRecord("example-fake", Start, Start.AddSeconds(3), CollectorOutcome.Failed, 0, 0, 2, [new("collector.all_checks_errored", "All 2 checks errored.")], null),
            TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(["status", "--config", config], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("example-fake failed: All 2 checks errored. (collector.all_checks_errored)");
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
    public async Task With_a_scope_lists_exceptions_expiring_within_60_days_and_expired_ones()
    {
        // s15-03: offline, from the expiry each exception declares; the approval itself is shown by scope plan, which reads CISO Assistant.
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);
        var scope = Directory.CreateDirectory(Path.Combine(_workDir, "scope")).FullName;
        await File.WriteAllTextAsync(Path.Combine(scope, "exceptions.yaml"), """
            exceptions:
              - {ref_id: EXC-01, name: Legacy finance app without MFA, domain: Example Pty Ltd, expiration_date: "2027-01-20"}
              - {ref_id: EXC-02, name: Old kiosk, domain: Example Pty Ltd, expiration_date: "2026-10-31"}
              - {ref_id: EXC-03, name: Lab printer, domain: Example Pty Ltd, expiration_date: "2027-06-30"}
            """, TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(
            ["status", "--config", config, "--scope", scope], TextReader.Null, _output, _error, new Dictionary<string, string?> { ["HOUND_TODAY"] = "2026-11-25" }, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldContain(l => l.StartsWith("EXCEPTION", StringComparison.Ordinal));
        lines.Single(l => l.StartsWith("EXC-01 ", StringComparison.Ordinal)).ShouldContain("in 56 days");
        lines.Single(l => l.StartsWith("EXC-02 ", StringComparison.Ordinal)).ShouldContain("expired 25 days ago");
        lines.ShouldNotContain(l => l.StartsWith("EXC-03", StringComparison.Ordinal), "it expires after the 60-day window");
    }

    [Fact]
    public async Task A_missing_config_exits_three()
    {
        var exitCode = await HoundCli.RunAsync(
            ["status", "--config", Path.Combine(_workDir, "missing.yaml")], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("missing.yaml");
    }

    [Fact]
    public async Task Gives_each_programmes_standing_from_the_latest_snapshot()
    {
        // s16-07: the standing of s15-14, from the newest snapshot: a row not assessed counts as not met, and the share rounds down.
        var config = await StandingConfigAsync();
        await SaveSnapshotAsync(Posture("2026-10", Start.AddMonths(-1), ComplianceResult.Compliant, ComplianceResult.Compliant, ComplianceResult.Compliant, ComplianceResult.NotApplicable));
        await SaveSnapshotAsync(Posture("2026-11", Start, ComplianceResult.Compliant, ComplianceResult.Compliant, ComplianceResult.NotAssessed, ComplianceResult.NotApplicable));
        var scope = Directory.CreateDirectory(Path.Combine(_workDir, "scope")).FullName;

        var exitCode = await HoundCli.RunAsync(["status", "--config", config, "--scope", scope], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        text.ShouldContain("Standing from the 2026-11 snapshot taken 2026-06-30 02:00 UTC:");
        text.ShouldContain("Example Essential Eight: Level met: none (level 0). Compliant 2 of 3 applicable (66%), counting rows not assessed as not met; assessed 3 of 4; not assessed: 1.");

        _output.GetStringBuilder().Clear();
        exitCode = await HoundCli.RunAsync(["status", "--config", config, "--scope", scope, "--json"], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var standing = JsonNode.Parse(_output.ToString())!["standing"]!;
        standing["snapshot"]!.GetValue<string>().ShouldBe("2026-11");
        var programme = standing["programmes"]!.AsArray().ShouldHaveSingleItem()!;
        (programme["id"]!.GetValue<string>(), programme["level_met"], programme["compliant"]!.GetValue<int>(), programme["applicable"]!.GetValue<int>(),
            programme["compliant_pct"]!.GetValue<int>(), programme["assessed"]!.GetValue<int>(), programme["total"]!.GetValue<int>(), programme["not_assessed"]!.GetValue<int>())
            .ShouldBe(("e8", null, 2, 3, 66, 3, 4, 1));
        JsonNode.Parse(_output.ToString())!["collectors"]!.AsArray().ShouldHaveSingleItem()!["collector"]!.GetValue<string>().ShouldBe("example-fake");
    }

    [Fact]
    public async Task Says_how_to_make_a_snapshot_when_there_is_none()
    {
        var config = await StandingConfigAsync();
        var scope = Directory.CreateDirectory(Path.Combine(_workDir, "scope")).FullName;

        var exitCode = await HoundCli.RunAsync(["status", "--config", config, "--scope", scope], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("No posture snapshot yet, so no programme standing. Take one with: hound report snapshot --period YYYY-MM --scope <scope> --config <hound.yaml>");

        _output.GetStringBuilder().Clear();
        await HoundCli.RunAsync(["status", "--config", config, "--scope", scope, "--json"], _output, _error, cancellationToken: TestContext.Current.CancellationToken);

        var standing = JsonNode.Parse(_output.ToString())!["standing"]!;
        standing["snapshot"].ShouldBeNull();
        standing["hint"]!.GetValue<string>().ShouldContain("hound report snapshot");
    }

    private async Task<string> StandingConfigAsync()
    {
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);
        return config;
    }

    private static PostureSnapshot Posture(string period, DateTimeOffset takenAt, params ComplianceResult[] results) => new(
        ReportingPeriod.Parse(period).Match(p => p, _ => throw new InvalidOperationException()),
        takenAt,
        [
            new ProgrammePosture(
                "e8", "Example Essential Eight", "urn:example:risk:framework:example-e8", 1, "EX-E8-2026", [new ImplementationGroup("ML1", "Maturity Level One")], [],
                [.. results.Select((r, i) => new RequirementPosture($"urn:example:req:{i}", $"R{i}", null, ["ML1"], r))], []),
        ],
        [], [], [], [], [], [], []);

    private Task SaveSnapshotAsync(PostureSnapshot snapshot)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_workDir, "evidence", "reports")).FullName;
        return File.WriteAllTextAsync(Path.Combine(folder, $"posture-{snapshot.Period}.json"), PostureSnapshotJson.Write(snapshot), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }
}
