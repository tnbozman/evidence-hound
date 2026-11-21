using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound status</c>: the latest run of each configured collector, from the local evidence store.</summary>
internal static class StatusCommand
{
    private static readonly string[] Header = ["COLLECTOR", "LAST RUN (UTC)", "OUTCOME", "PASSED", "FAILED", "PUBLISHED"];
    private static readonly string[] AuditHeader = ["AUDIT", "LAST RUN (UTC)", "RUN ID", "CHANGED", "UNCHANGED", "DRIFT"];

    public static Command Build(TextWriter output, ILoggerFactory loggers, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.status");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var status = new Command("status", "Show the latest run of each configured collector.") { config };
        status.SetAction(async (parseResult, cancellationToken) =>
        {
            var deployment = await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken);
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

                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return status;
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
