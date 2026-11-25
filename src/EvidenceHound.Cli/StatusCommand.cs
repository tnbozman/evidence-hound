using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
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
            Description = "The compliance repo's scope/ folder: also list security exceptions that expire within 60 days or have expired.",
        };
        var status = new Command("status", "Show the latest run of each configured collector.") { config, scope };
        status.SetAction(async (parseResult, cancellationToken) =>
        {
            var loaded = await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken);
            var deployment = await loaded.Match(
                async d => Result.Ok(await AuditSeries.ResolveAsync(d, AuditSeries.Today(configuration), cancellationToken)),
                problems => Task.FromResult(Result.Fail<DeploymentConfig>(problems)));
            var latest = await deployment.BindAsync(d => new FileRunRecordStore(d.Evidence.Root).LatestAsync(cancellationToken));
            var history = await deployment.BindAsync(d => new FileRunHistory(d.Evidence.Root).LatestAsync(cancellationToken));
            return await deployment.Bind(d => latest.Bind(records => history.Map(entries => (Collectors: Rows(d, records), Audits: AuditRows(d, entries), Any: entries.Count > 0)))).Match(
                async tables =>
                {
                    await output.WriteAsync(Table([Header, .. tables.Collectors]));
                    if (tables.Any)
                    {
                        await output.WriteLineAsync();
                        await output.WriteAsync(Table([AuditHeader, .. tables.Audits]));
                    }

                    return parseResult.GetValue(scope) is { } dir ? await ExceptionsAsync(dir, AuditSeries.Today(configuration), output, logger, cancellationToken) : ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return status;
    }

    /// <summary>The exceptions <c>scope/exceptions.yaml</c> declares that expire within 60 days or have expired (s15-03), by their declared expiry.</summary>
    private static async Task<int> ExceptionsAsync(DirectoryInfo scope, DateOnly today, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var path = Path.Combine(scope.FullName, "exceptions.yaml");
        if (!File.Exists(path))
        {
            return ExitCodes.Ok;
        }

        var (exceptions, problems) = ScopeLoader.ParseExceptions(new ScopeSource(path, await File.ReadAllTextAsync(path, cancellationToken)));
        if (problems.Count > 0)
        {
            return Log.Problems(logger, [.. problems.Select(p => new Problem(p.Code, $"{p.At.File}:{p.At.Line}: {p.Message} {p.Hint}"))]);
        }

        var due = ExceptionReview.Declared(exceptions, today);
        await output.WriteLineAsync();
        await output.WriteAsync(due.Count == 0
            ? "No security exception expires within 60 days.\n"
            : Table([ExceptionHeader, .. due.Select(n => new[] { n.RefId, n.Expires ?? "-", n.Detail })]));
        return ExitCodes.Ok;
    }

    private static IReadOnlyList<string[]> Rows(DeploymentConfig config, IReadOnlyList<RunRecord> records)
    {
        var byName = records.ToDictionary(r => r.Collector, StringComparer.Ordinal);
        return [.. config.Collectors.Select(c => byName.TryGetValue(c.Name, out var r) ? Row(r) : [c.Name, "never run", "-", "-", "-", "-"])];
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
