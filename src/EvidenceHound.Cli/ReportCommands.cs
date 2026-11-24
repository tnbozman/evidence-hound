using System.CommandLine;
using System.Text;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound report</c>: posture snapshots and the committee and board reports built from them (s12-04 to s12-06).</summary>
internal static class ReportCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.report");
        var period = new Option<string>("--period") { Description = "The period: YYYY-MM for a month, YYYY-Qn for a quarter.", Required = true };
        var scope = new Option<DirectoryInfo?>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var config = new Option<FileInfo>("--config") { Description = "The deployment config: the evidence store, the run store and the CISO Assistant connection.", Required = true };
        var control = new Option<string?>("--control") { Description = "The ref_id of the applied control the snapshot is uploaded to as evidence, such as your 'Security reporting' control. Without it nothing is written to CISO Assistant." };
        var snapshot = new Command(
            "snapshot",
            "Read CISO Assistant and the local run store once, and write the period's posture snapshot as JSON to the evidence store. Reports are built from snapshots.")
        {
            period, scope, config, control,
        };
        snapshot.SetAction((parseResult, cancellationToken) => SnapshotAsync(
            parseResult.GetRequiredValue(period),
            parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope")),
            parseResult.GetRequiredValue(config),
            parseResult.GetValue(control),
            output,
            loggers,
            logger,
            configuration,
            registry,
            cancellationToken));
        var month = new Option<string>("--month") { Description = "The month, YYYY-MM.", Required = true };
        var reportConfig = new Option<FileInfo>("--config") { Description = "The deployment config: where the evidence store keeps the snapshots.", Required = true };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the report (Markdown) to this file instead of the output." };
        var committee = new Command(
            "committee",
            "The monthly security committee report (Markdown) from the month's snapshot and the previous month's. Reads no CISO Assistant.")
        {
            month, reportConfig, outFile,
        };
        committee.SetAction((parseResult, cancellationToken) => CommitteeAsync(
            parseResult.GetRequiredValue(month), parseResult.GetRequiredValue(reportConfig), parseResult.GetValue(outFile), output, logger, registry, cancellationToken));
        var quarter = new Option<string>("--quarter") { Description = "The quarter, YYYY-Qn.", Required = true };
        var boardConfig = new Option<FileInfo>("--config") { Description = "The deployment config: where the evidence store keeps the snapshots.", Required = true };
        var boardOut = new Option<FileInfo?>("--out") { Description = "Write the report (Markdown) to this file instead of the output." };
        var board = new Command(
            "board",
            "The quarterly board report (Markdown) from the snapshots of the last month of this quarter and the three before it. Reads no CISO Assistant.")
        {
            quarter, boardConfig, boardOut,
        };
        board.SetAction((parseResult, cancellationToken) => BoardAsync(
            parseResult.GetRequiredValue(quarter), parseResult.GetRequiredValue(boardConfig), parseResult.GetValue(boardOut), output, logger, registry, cancellationToken));
        return new Command("report", "Posture snapshots, and the monthly committee and quarterly board reports built from them.") { snapshot, committee, board };
    }

    private static async Task<int> CommitteeAsync(
        string monthText, FileInfo configFile, FileInfo? outFile, TextWriter output, ILogger logger, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var period = ReportingPeriod.Parse(monthText).Bind(p => p.Kind == PeriodKind.Month
            ? Result.Ok(p)
            : Result.Fail<ReportingPeriod>(new Problem("report.period_not_month", $"'{monthText}' is a quarter: the committee report is monthly (YYYY-MM); the board report is quarterly.")));
        var deployment = await ConfigCommands.LoadAsync(configFile, registry, cancellationToken);
        var loaded = await period.Bind(p => deployment.Map(d => (Period: p, Root: d.Evidence.Root))).BindAsync(async x =>
            (await ReadSnapshotAsync(x.Root, x.Period, cancellationToken))
                .Bind(current => ReadSnapshotsAsync(x.Root, [x.Period.Previous()], cancellationToken).Result.Map(previous => (Current: current, Previous: previous.SingleOrDefault()))));
        return await loaded.Match(
            async snapshots => await WriteReportAsync(CommitteeReport.Render(snapshots.Current, snapshots.Previous), outFile, output, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> BoardAsync(
        string quarterText, FileInfo configFile, FileInfo? outFile, TextWriter output, ILogger logger, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var period = ReportingPeriod.Parse(quarterText).Bind(p => p.Kind == PeriodKind.Quarter
            ? Result.Ok(p)
            : Result.Fail<ReportingPeriod>(new Problem("report.period_not_quarter", $"'{quarterText}' is a month: the board report is quarterly (YYYY-Qn); the committee report is monthly.")));
        var deployment = await ConfigCommands.LoadAsync(configFile, registry, cancellationToken);
        var loaded = await period.Bind(p => deployment.Map(d => (Quarter: p, Root: d.Evidence.Root))).BindAsync(async x =>
        {
            var ends = Enumerable.Range(0, BoardReport.TrendQuarters)
                .Select(i => new ReportingPeriod(PeriodKind.Month, x.Quarter.End.AddMonths(-3 * i).Year, x.Quarter.End.AddMonths(-3 * i).Month))
                .Reverse()
                .ToList();
            return (await ReadSnapshotAsync(x.Root, ends[^1], cancellationToken))
                .Bind(_ => ReadSnapshotsAsync(x.Root, ends, cancellationToken).Result)
                .Map(snapshots => (x.Quarter, Snapshots: snapshots));
        });
        return await loaded.Match(
            async x => await WriteReportAsync(BoardReport.Render(x.Quarter, x.Snapshots), outFile, output, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>A period's snapshot from the evidence store's <c>reports/</c> folder; an error that says how to take it when there is none.</summary>
    internal static async Task<Result<PostureSnapshot>> ReadSnapshotAsync(string root, ReportingPeriod period, CancellationToken cancellationToken)
    {
        var file = SnapshotFile(root, period);
        return File.Exists(file)
            ? PostureSnapshotJson.Read(await File.ReadAllTextAsync(file, cancellationToken), file)
            : Result.Fail<PostureSnapshot>(new Problem(
                "report.snapshot_missing", $"No snapshot for {period} in {Path.GetDirectoryName(file)}. Take it with: hound report snapshot --period {period} --config <hound.yaml>"));
    }

    /// <summary>The snapshots of those <paramref name="periods"/> the evidence store holds, in order; missing ones are skipped.</summary>
    internal static async Task<Result<IReadOnlyList<PostureSnapshot>>> ReadSnapshotsAsync(string root, IEnumerable<ReportingPeriod> periods, CancellationToken cancellationToken)
    {
        var found = new List<Result<PostureSnapshot>>();
        foreach (var period in periods.Where(p => File.Exists(SnapshotFile(root, p))))
        {
            found.Add(await ReadSnapshotAsync(root, period, cancellationToken));
        }

        return Result.Combine(found);
    }

    private static string SnapshotFile(string root, ReportingPeriod period) => Path.Combine(root, "reports", SnapshotName(period) + ".json");

    private static async Task<int> WriteReportAsync(string report, FileInfo? outFile, TextWriter output, CancellationToken cancellationToken)
    {
        if (outFile is null)
        {
            await output.WriteAsync(report);
            return ExitCodes.Ok;
        }

        await File.WriteAllTextAsync(outFile.FullName, report, cancellationToken);
        await output.WriteLineAsync($"wrote {outFile.FullName}");
        return ExitCodes.Ok;
    }

    /// <summary>The snapshot's file name in the evidence store's <c>reports/</c> folder, and its evidence ref_id after <c>hound-report/</c>.</summary>
    internal static string SnapshotName(ReportingPeriod period) => $"posture-{period}";

    private static async Task<int> SnapshotAsync(
        string periodText, DirectoryInfo scopeDir, FileInfo configFile, string? control, TextWriter output, ILoggerFactory loggers, ILogger logger,
        IConfiguration configuration, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        if (ReportingPeriod.Parse(periodText) is Result<ReportingPeriod>.Fail badPeriod)
        {
            return Log.Problems(logger, badPeriod.Errors);
        }

        var period = ReportingPeriod.Parse(periodText).Match(p => p, _ => throw new InvalidOperationException());
        var today = AuditSeries.Today(configuration);
        var checkedScope = await ScopeCommands.ValidateAsync(scopeDir, null, registry, today, cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
        }

        var deployment = await ConfigCommands.LoadAsync(configFile, registry, cancellationToken);
        var connection = await PublishCommand.ConnectionAsync(configuration, configFile, registry, cancellationToken);
        if (deployment.Bind(d => connection.Map(c => (d, c))) is not Result<(DeploymentConfig Deployment, CisoAssistantOptions Connection)>.Ok { Value: var setup })
        {
            return Log.Problems(logger, [.. deployment.Match(_ => [], ps => ps), .. connection.Match(_ => [], ps => ps)]);
        }

        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(setup.Connection).BuildServiceProvider();
        var client = services.GetRequiredService<ICisoAssistantClient>();
        var posture = services.GetRequiredService<IPostureSource>();
        var cycles = await AuditSeries.CyclesAsync(scopeDir.FullName, cancellationToken);
        var programmes = new List<Result<ProgrammePosture>>();
        foreach (var programme in checkedScope.Scope.Setup.Programmes)
        {
            var coverage = await CoverageAsync(programme, checkedScope, setup.Deployment, cancellationToken);
            programmes.Add((await ProgrammeAsync(programme, checkedScope, cycles, period, client, posture, cancellationToken))
                .Bind(p => coverage.Map(c => p with { Coverage = c })));
        }

        var records = await posture.ReadAsync(cancellationToken);
        var collectors = await new FileRunRecordStore(setup.Deployment.Evidence.Root).LatestAsync(cancellationToken);
        var built = Result.Combine(programmes).Bind(p => records.Bind(r => collectors.Map(c => new PostureSnapshot(
            period, DateTimeOffset.UtcNow, p, r.Controls, r.Acceptances, r.Remediation, r.Incidents, r.Risks, [.. c.Select(Health)], r.Due)
        {
            Warnings = [.. checkedScope.Warnings.Select(w => new ScopeWarning(w.Code, w.ToProblem().Message))],
        })));
        if (built is not Result<PostureSnapshot>.Ok { Value: var taken })
        {
            return Log.Problems(logger, built.Match(_ => [], ps => ps));
        }

        var json = PostureSnapshotJson.Write(taken);
        var folder = Path.Combine(setup.Deployment.Evidence.Root, "reports");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, SnapshotName(period) + ".json");
        await File.WriteAllTextAsync(file, json, cancellationToken);
        await output.WriteLineAsync($"wrote {file}: {taken.Programmes.Count} programmes, {taken.Programmes.Sum(p => p.Requirements.Count)} requirement rows");
        if (control is null)
        {
            return ExitCodes.Ok;
        }

        var uploaded = await UploadAsync(client, period, control, json, cancellationToken);
        return await uploaded.Match(
            async _ =>
            {
                await output.WriteLineAsync($"uploaded hound-report/{SnapshotName(period)} to applied control {control}");
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<Result<ProgrammePosture>> ProgrammeAsync(
        ScopeProgramme programme, ScopeCommands.CheckedScope checkedScope, IReadOnlyDictionary<string, AuditCycle> cycles, ReportingPeriod period,
        ICisoAssistantClient client, IPostureSource posture, CancellationToken cancellationToken)
    {
        var framework = checkedScope.Inputs.Snapshots[programme.Framework.Value];
        var auditRef = AuditPeriods.Target(programme.Audit.Value, cycles, period.End);
        var audit = await client.FindAuditAsync(auditRef, cancellationToken);
        var rows = await client.ListRequirementAssessmentsAsync(auditRef, cancellationToken);
        return audit.Bind(a => rows.Map(r => Posture.Programme(programme.Id, programme.Name, framework, auditRef, programme.Groups, r) with
        {
            Link = posture.Link("compliance-assessments", a.Id),
            Closed = a.Closed,
            Register = Register(programme, checkedScope.Scope.Setup, period),
        }));
    }

    /// <summary>How the config's mapping for the programme's audit answers each parent and group; none when no target maps that audit.</summary>
    private static async Task<Result<IReadOnlyList<CoverageCount>>> CoverageAsync(
        ScopeProgramme programme, ScopeCommands.CheckedScope checkedScope, DeploymentConfig deployment, CancellationToken cancellationToken)
    {
        if (deployment.Targets.FirstOrDefault(t => t.ComplianceAssessmentRefId == programme.Audit.Value) is not { } target)
        {
            return Result.Ok<IReadOnlyList<CoverageCount>>([]);
        }

        var manual = MappingCrosswalkCommand.ManualChecks(deployment);
        var framework = checkedScope.Inputs.Snapshots[programme.Framework.Value];
        return (await MappingCommands.LoadAsync(new FileInfo(target.MappingPath), cancellationToken))
            .Map(loaded => MappingCoverage.Count(framework, loaded.Mapping, loaded.ManualOnly.Select(m => m.Key), manual, programme.Groups));
    }

    private static RegisterSnapshot? Register(ScopeProgramme programme, ScopeSetup setup, ReportingPeriod period)
    {
        if (programme.Register is not { } register)
        {
            return null;
        }

        var summary = RegisterSummary.Of(RegisterItems.Of(setup, register.Of), register.By, period.Start, period.End, new HashSet<string>(StringComparer.Ordinal));
        return new RegisterSnapshot(register.Of, summary.Total, summary.Counts) { Added = summary.Added, Retired = summary.Retired };
    }

    private static CollectorHealth Health(RunRecord record) => new(
        record.Collector,
        record.EndedAt,
        record.Outcome.ToString(),
        record.ChecksPassed,
        record.ChecksFailed,
        [.. record.Problems.Select(p => p.Message)]);

    private static async Task<Result<Unit>> UploadAsync(ICisoAssistantClient client, ReportingPeriod period, string control, string json, CancellationToken cancellationToken)
    {
        var name = SnapshotName(period);
        var evidence = await client.FindOrCreateEvidenceAsync($"hound-report/{name}", $"Posture snapshot {period}", cancellationToken);
        return await evidence.BindAsync(async e =>
            (await client.UploadEvidenceRevisionAsync(e.Id, new EvidenceFile(name + ".json", Encoding.UTF8.GetBytes(json)), cancellationToken))
                .Map(_ => e.Id))
            .BindAsync(id => client.LinkEvidenceToAppliedControlAsync(control, id, cancellationToken));
    }
}
