using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound status</c>: the latest run of each configured collector, from the local evidence store.</summary>
internal static class StatusCommand
{
    private static readonly string[] Header = ["COLLECTOR", "LAST RUN (UTC)", "OUTCOME", "PASSED", "FAILED", "PUBLISHED"];

    public static Command Build(TextWriter output, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.status");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var status = new Command("status", "Show the latest run of each configured collector.") { config };
        status.SetAction(async (parseResult, cancellationToken) =>
        {
            var deployment = await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), cancellationToken);
            var latest = await deployment.BindAsync(d => new FileRunRecordStore(d.Evidence.Root).LatestAsync(cancellationToken));
            return await deployment.Bind(d => latest.Map(records => Rows(d, records))).Match(
                async rows =>
                {
                    await output.WriteAsync(Table([Header, .. rows]));
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
        var widths = Enumerable.Range(0, Header.Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        return string.Concat(rows.Select(r => string.Join("  ", r.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd() + Environment.NewLine));
    }
}
