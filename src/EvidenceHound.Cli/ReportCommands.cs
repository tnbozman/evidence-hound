using System.CommandLine;
using System.Text;
using EvidenceHound.CisoAssistant;
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
        return new Command("report", "Posture snapshots, and the monthly committee and quarterly board reports built from them.") { snapshot };
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
            programmes.Add(await ProgrammeAsync(programme, checkedScope, cycles, period, client, posture, cancellationToken));
        }

        var records = await posture.ReadAsync(cancellationToken);
        var collectors = await new FileRunRecordStore(setup.Deployment.Evidence.Root).LatestAsync(cancellationToken);
        var built = Result.Combine(programmes).Bind(p => records.Bind(r => collectors.Map(c => new PostureSnapshot(
            period, DateTimeOffset.UtcNow, p, r.Controls, r.Acceptances, r.Remediation, r.Incidents, r.Risks, [.. c.Select(Health)], r.Due))));
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
