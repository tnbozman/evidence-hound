using EvidenceHound.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s12-05: <c>hound report committee</c> renders the month's report from the snapshots in the evidence store.</summary>
public sealed class ReportCommitteeCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-committee-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ReportCommitteeCommandTests()
    {
        File.WriteAllText(Path.Combine(_dir, "hound.yaml"), $"""
            ciso_assistant:
              url: https://ciso.example.com/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {Path.Combine(_dir, "evidence")}
            collectors: []
            """);
        Directory.CreateDirectory(Path.Combine(_dir, "evidence", "reports"));
    }

    [Fact]
    public async Task Renders_the_month_against_the_previous_snapshot()
    {
        await Save(Snapshot("2026-10", "in_progress"));
        await Save(Snapshot("2026-11", "active"));
        var outFile = Path.Combine(_dir, "committee.md");

        var exitCode = await Run("report", "committee", "--month", "2026-11", "--config", Path.Combine(_dir, "hound.yaml"), "--out", outFile);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var report = await File.ReadAllTextAsync(outFile, _ct);
        report.ShouldStartWith("# Security committee report: November 2026");
        report.ShouldContain("compared with 2026-10");
        report.ShouldContain("AC-MFA MFA: in_progress → active");
    }

    [Fact]
    public async Task Without_a_previous_snapshot_the_report_says_so_and_prints_to_the_output()
    {
        await Save(Snapshot("2026-11", "active"));

        var exitCode = await Run("report", "committee", "--month", "2026-11", "--config", Path.Combine(_dir, "hound.yaml"));

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("there is no previous snapshot to compare with");
    }

    [Fact]
    public async Task A_missing_snapshot_says_how_to_take_one()
    {
        var exitCode = await Run("report", "committee", "--month", "2026-11", "--config", Path.Combine(_dir, "hound.yaml"));

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("hound report snapshot --period 2026-11");
    }

    [Fact]
    public async Task A_quarter_is_not_a_month()
    {
        var exitCode = await Run("report", "committee", "--month", "2026-Q4", "--config", Path.Combine(_dir, "hound.yaml"));

        exitCode.ShouldBe(ExitCodes.Configuration);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static PostureSnapshot Snapshot(string period, string mfa) => new(
        ReportingPeriod.Parse(period).Match(p => p, _ => throw new InvalidOperationException()),
        DateTimeOffset.UnixEpoch,
        [],
        [new ControlItem("AC-MFA", "MFA", mfa, null, null)],
        [],
        [],
        [],
        [],
        [],
        []);

    private Task Save(PostureSnapshot snapshot) =>
        File.WriteAllTextAsync(Path.Combine(_dir, "evidence", "reports", $"posture-{snapshot.Period}.json"), PostureSnapshotJson.Write(snapshot), _ct);

    private Task<int> Run(params string[] args) => HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), _ct);
}
