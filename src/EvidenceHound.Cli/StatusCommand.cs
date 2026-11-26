using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using EvidenceHound.Runtime.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound status</c>: the latest run of each configured collector, from the local evidence store, and each target audit's last run. A target
/// that names an audit series shows the period open today, the audit <c>hound run</c> publishes to.
/// </summary>
internal static class StatusCommand
{
    private static readonly string[] Header = ["COLLECTOR", "LAST RUN (UTC)", "OUTCOME", "PASSED", "FAILED", "PUBLISHED"];
    private static readonly string[] AuditHeader = ["AUDIT", "LAST RUN (UTC)", "RUN ID", "CHANGED", "UNCHANGED", "DRIFT"];
    private static readonly string[] ExceptionHeader = ["EXCEPTION", "EXPIRES", "NOTE"];

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.status");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var scope = new Option<DirectoryInfo>("--scope")
        {
            Description = "The compliance repo's scope/ folder: also give each programme's standing from the latest posture snapshot, and list security exceptions that expire within 60 days or have expired.",
        };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent: the same figures as the tables." };
        var status = new Command("status", "Show the latest run of each configured collector, and with --scope each programme's standing.") { config, scope, json };
        status.SetAction(async (parseResult, cancellationToken) =>
        {
            var today = AuditSeries.Today(configuration);
            var loaded = await DeploymentFile.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken);
            var deployment = await loaded.Match(
                async d => Result.Ok(await AuditSeries.ResolveAsync(d, today, cancellationToken)),
                problems => Task.FromResult(Result.Fail<DeploymentConfig>(problems)));
            var latest = await deployment.BindAsync(d => new FileRunRecordStore(d.Evidence.Root).LatestAsync(cancellationToken));
            var history = await deployment.BindAsync(d => new FileRunHistory(d.Evidence.Root).LatestAsync(cancellationToken));
            var dir = parseResult.GetValue(scope);
            var view = await deployment.Bind(d => latest.Bind(records => history.Map(entries => (Deployment: d, Records: records, Entries: entries))))
                .BindAsync(async x =>
                {
                    var exceptions = dir is null ? Result.Ok<IReadOnlyList<ExceptionNote>>([]) : await ExceptionsAsync(dir, today, cancellationToken);
                    var standing = dir is null ? null : await StandingAsync(x.Deployment.Evidence.Root, cancellationToken);
                    var health = parseResult.GetValue(json) ? await HealthAsync(x.Deployment, parseResult.GetRequiredValue(config), configuration, registry, today, cancellationToken) : null;
                    return exceptions.Map(notes => new StatusView(x.Deployment, x.Records, x.Entries, dir is null ? null : notes, standing) { Health = health });
                });
            return await view.Match(
                async v =>
                {
                    await output.WriteAsync(parseResult.GetValue(json) ? Json(v) : Text(v));
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return status;
    }

    /// <summary>What <c>hound status</c> shows: runs, audits, and with a scope folder the exceptions due and the programmes' standing.</summary>
    private sealed record StatusView(
        DeploymentConfig Deployment, IReadOnlyList<RunRecord> Records, IReadOnlyList<(string Audit, HistoryEntry Entry)> Entries, IReadOnlyList<ExceptionNote>? Exceptions, PostureSnapshot? Standing)
    {
        public bool ScopeGiven => Exceptions is not null;

        /// <summary>For <c>--json</c> (s18-05): each collector's latest checks, the CISO Assistant connection and the manual sources.</summary>
        public Health? Health { get; init; }
    }

    private sealed record Health(
        IReadOnlyDictionary<string, IReadOnlyList<(string Check, string Outcome)>> Checks, HoundHealth.Connection Connection, IReadOnlyList<HoundHealth.ManualDue> Manual);

    /// <summary>The latest checks each collector saved (their ids and outcomes only, never observations or evidence), the connection and manual sources.</summary>
    private static async Task<Health> HealthAsync(
        DeploymentConfig deployment, FileInfo config, IConfiguration configuration, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken)
    {
        var saved = await new FileLatestResults(deployment.Evidence.Root, [.. deployment.Collectors.Select(c => c.Name)]).OthersAsync("", cancellationToken);
        var checks = saved.ToDictionary(
            l => l.Collector,
            l => (IReadOnlyList<(string, string)>)[.. l.Results.Checks.Select(c => (c.CheckId.Value, c.Outcome.ToString().ToLowerInvariant())).Order()],
            StringComparer.Ordinal);
        var (connection, _) = await HoundHealth.ConnectionAsync(configuration, config, registry, cancellationToken);
        return new Health(checks, connection, await HoundHealth.ManualSourcesAsync(deployment, today, cancellationToken));
    }

    private static string Text(StatusView view)
    {
        var text = new StringBuilder(Table([Header, .. Rows(view.Deployment, view.Records)]));
        foreach (var failure in Failures(view.Deployment, view.Records))
        {
            text.AppendLine(failure);
        }

        if (view.Entries.Count > 0)
        {
            text.AppendLine().Append(Table([AuditHeader, .. AuditRows(view.Deployment, view.Entries)]));
        }

        if (!view.ScopeGiven)
        {
            return text.ToString();
        }

        text.AppendLine();
        if (view.Standing is not { } snapshot)
        {
            text.AppendLine(NoSnapshot);
        }
        else
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Standing from the {snapshot.Period} snapshot taken {snapshot.TakenAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC:");
            foreach (var programme in snapshot.Programmes)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {programme.Name}: {Standing.Of(programme).Text()}");
            }
        }

        text.AppendLine();
        text.Append(view.Exceptions!.Count == 0
            ? "No security exception expires within 60 days.\n"
            : Table([ExceptionHeader, .. view.Exceptions.Select(n => new[] { n.RefId, n.Expires ?? "-", n.Detail })]));
        return text.ToString();
    }

    private static string Json(StatusView view)
    {
        var byName = view.Records.ToDictionary(r => r.Collector, StringComparer.Ordinal);
        var byAudit = view.Entries.ToDictionary(e => e.Audit, e => e.Entry, StringComparer.Ordinal);
        var json = new JsonObject
        {
            ["collectors"] = new JsonArray([.. view.Deployment.Collectors.Select(c => byName.TryGetValue(c.Name, out var r)
                ? new JsonObject
                {
                    ["collector"] = c.Name,
                    ["last_run"] = r.EndedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["outcome"] = r.Outcome.ToString().ToLowerInvariant(),
                    ["passed"] = r.ChecksPassed,
                    ["failed"] = r.ChecksFailed,
                    ["problems"] = new JsonArray([.. r.Problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
                    ["publish_failed"] = r.Publish?.Failed ?? 0,
                    ["checks"] = Checks(view, c.Name),
                }
                : new JsonObject { ["collector"] = c.Name, ["last_run"] = null, ["outcome"] = "never_run" })]),
            ["audits"] = new JsonArray([.. view.Deployment.Targets.Select(t => t.ComplianceAssessmentRefId).Distinct(StringComparer.Ordinal).Select(audit => byAudit.TryGetValue(audit, out var e)
                ? new JsonObject
                {
                    ["audit"] = audit,
                    ["last_run"] = e.Date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["run_id"] = e.RunId,
                    ["changed"] = e.Changed,
                    ["unchanged"] = e.Unchanged,
                    ["drift"] = e.Drift,
                }
                : new JsonObject { ["audit"] = audit, ["last_run"] = null })]),
        };
        if (view.Health is { } health)
        {
            json["ciso_assistant"] = new JsonObject
            {
                ["reachable"] = health.Connection.Reachable,
                ["token"] = health.Connection.Token,
                ["token_expiry"] = null,
                ["note"] = health.Connection.Note,
                ["rejected_publishes"] = view.Records.Sum(r => r.Publish?.Failed ?? 0),
            };
            json["manual_sources"] = new JsonArray([.. health.Manual.Select(m => new JsonObject
            {
                ["source"] = m.Source,
                ["owner"] = m.Owner,
                ["every_days"] = m.EveryDays,
                ["last_evidence"] = m.LastEvidence?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["due"] = m.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["state"] = m.State,
            })]);
        }

        if (view.ScopeGiven)
        {
            json["standing"] = view.Standing is { } snapshot
                ? new JsonObject
                {
                    ["snapshot"] = snapshot.Period.ToString(),
                    ["taken_at"] = snapshot.TakenAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["programmes"] = new JsonArray([.. snapshot.Programmes.Select(p => (Programme: p, Standing: Standing.Of(p))).Select(x => new JsonObject
                    {
                        ["id"] = x.Programme.Id,
                        ["name"] = x.Programme.Name,
                        ["audit"] = x.Programme.Audit,
                        ["level_met"] = x.Standing.Met?.Name,
                        ["compliant"] = x.Standing.Compliant,
                        ["applicable"] = x.Standing.Applicable,
                        ["compliant_pct"] = x.Standing.Applicable == 0 ? null : (int)(100L * x.Standing.Compliant / x.Standing.Applicable),
                        ["assessed"] = x.Standing.Assessed,
                        ["total"] = x.Standing.Total,
                        ["not_assessed"] = x.Standing.NotAssessed,
                    })]),
                }
                : new JsonObject { ["snapshot"] = null, ["hint"] = NoSnapshot };
            json["exceptions_due"] = new JsonArray([.. view.Exceptions!.Select(n => new JsonObject { ["ref_id"] = n.RefId, ["expires"] = n.Expires, ["note"] = n.Detail })]);
        }

        return json.ToJsonString(CommandJson.Escaped) + Environment.NewLine;
    }

    private static JsonArray Checks(StatusView view, string collector) => view.Health?.Checks.GetValueOrDefault(collector) is { } checks
        ? new JsonArray([.. checks.Select(c => new JsonObject { ["check"] = c.Check, ["outcome"] = c.Outcome })])
        : [];


    private static readonly string NoSnapshot =
        "No posture snapshot yet, so no programme standing. Take one with: hound report snapshot --period YYYY-MM --scope <scope> --config <hound.yaml>";

    /// <summary>The newest posture snapshot in the evidence store's <c>reports/</c> folder, by when it was taken; null when there is none.</summary>
    private static async Task<PostureSnapshot?> StandingAsync(string root, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, "reports");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        PostureSnapshot? newest = null;
        foreach (var file in Directory.GetFiles(folder, "posture-*.json"))
        {
            if (PostureSnapshotJson.Read(await File.ReadAllTextAsync(file, cancellationToken), file) is Result<PostureSnapshot>.Ok { Value: var snapshot }
                && (newest is null || snapshot.TakenAt > newest.TakenAt))
            {
                newest = snapshot;
            }
        }

        return newest;
    }

    /// <summary>The exceptions <c>scope/exceptions.yaml</c> declares that expire within 60 days or have expired (s15-03), by their declared expiry.</summary>
    internal static async Task<Result<IReadOnlyList<ExceptionNote>>> ExceptionsAsync(DirectoryInfo scope, DateOnly today, CancellationToken cancellationToken)
    {
        var path = Path.Combine(scope.FullName, "exceptions.yaml");
        if (!File.Exists(path))
        {
            return Result.Ok<IReadOnlyList<ExceptionNote>>([]);
        }

        var (exceptions, problems) = ScopeLoader.ParseExceptions(new ScopeSource(path, await File.ReadAllTextAsync(path, cancellationToken)));
        return problems.Count > 0
            ? Result.Fail<IReadOnlyList<ExceptionNote>>([.. problems.Select(p => new Problem(p.Code, $"{p.At.File}:{p.At.Line}: {p.Message} {p.Hint}"))])
            : Result.Ok(ExceptionReview.Declared(exceptions, today));
    }

    private static IReadOnlyList<string[]> Rows(DeploymentConfig config, IReadOnlyList<RunRecord> records)
    {
        var byName = records.ToDictionary(r => r.Collector, StringComparer.Ordinal);
        return [.. config.Collectors.Select(c => byName.TryGetValue(c.Name, out var r) ? Row(r) : [c.Name, "never run", "-", "-", "-", "-"])];
    }

    /// <summary>Why each configured collector's latest run failed (s15-D3), so a run that proved nothing is not read as a quiet "0 0".</summary>
    private static IReadOnlyList<string> Failures(DeploymentConfig config, IReadOnlyList<RunRecord> records)
    {
        var names = config.Collectors.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        return [.. records.Where(r => r.Outcome == CollectorOutcome.Failed && names.Contains(r.Collector))
            .SelectMany(r => r.Problems.Select(p => $"{r.Collector} failed: {p.Message} ({p.Code})"))];
    }

    private static IReadOnlyList<string[]> AuditRows(DeploymentConfig config, IReadOnlyList<(string Audit, HistoryEntry Entry)> entries)
    {
        var byAudit = entries.ToDictionary(e => e.Audit, e => e.Entry, StringComparer.Ordinal);
        return [.. config.Targets.Select(t => t.ComplianceAssessmentRefId).Distinct(StringComparer.Ordinal).Select(audit => byAudit.TryGetValue(audit, out var e)
            ? [audit, Time(e.Date), e.RunId, Count(e.Changed), Count(e.Unchanged), Count(e.Drift)]
            : new[] { audit, "never run", "-", "-", "-", "-" })];
    }

    private static string Time(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string[] Row(RunRecord r) =>
    [
        r.Collector,
        r.EndedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        r.Outcome.ToString().ToLowerInvariant(),
        r.ChecksPassed.ToString(CultureInfo.InvariantCulture),
        r.ChecksFailed.ToString(CultureInfo.InvariantCulture),
        r.Publish is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Sent} sent, {p.Skipped} skipped, {p.Failed} failed") : "-",
    ];

    private static string Table(IReadOnlyList<string[]> rows)
    {
        var widths = Enumerable.Range(0, rows[0].Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        return string.Concat(rows.Select(r => string.Join("  ", r.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd() + Environment.NewLine));
    }
}
