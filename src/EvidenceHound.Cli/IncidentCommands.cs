using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>s15-07: <c>hound incident obligations</c>, the reports an incident recorded in CISO Assistant owes and when each is due.</summary>
internal static class IncidentCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.incident");
        var incident = new Argument<string>("incident") { Description = "The incident's reference ID in CISO Assistant, such as INC-2026-01." };
        var scope = new Option<DirectoryInfo?>("--scope") { Description = "The scope folder with reporting.yaml and setup.yaml. Default: scope/ in the current folder." };
        var config = new Option<FileInfo>("--config") { Description = "The deployment config: the CISO Assistant connection.", Required = true };
        var format = new Option<string>("--format") { Description = "text (the default) or json.", DefaultValueFactory = _ => "text" };
        var obligations = new Command(
            "obligations",
            "List each report the incident owes under scope/reporting.yaml, due from its discovery (its first detection entry). A timeline entry 'Reported: <id>, ...' marks a report as made. Reads CISO Assistant; writes nothing.")
        {
            incident, scope, config, format,
        };
        obligations.SetAction((parseResult, cancellationToken) => ObligationsAsync(
            parseResult.GetRequiredValue(incident),
            parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope")),
            parseResult.GetRequiredValue(config),
            parseResult.GetValue(format) == "json",
            output,
            loggers,
            logger,
            configuration,
            registry,
            cancellationToken));
        var dfars = new Option<bool>("--dfars") { Description = "The DFARS 252.204-7012 cyber incident report: 72 hours from discovery, filed on the DoD portal." };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the pack to this file instead of printing it. Keep it out of the repo: it describes the incident." };
        var reportPack = new Command(
            "report-pack",
            "Gather what a report asks for from CISO Assistant and the scope, as Markdown for the person who files it: affected systems and their data, suppliers, the contracts that carry the clause, the deadline and the timeline. Reads only; files nothing.")
        {
            incident, scope, config, dfars, outFile,
        };
        reportPack.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!parseResult.GetValue(dfars))
            {
                return Log.Problems(logger, [new Problem("incident.report_kind_missing", "Say which report the pack is for: --dfars (DFARS 252.204-7012) is the one hound gathers.")]);
            }

            var scopeDir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
            var read = await ReadAsync(parseResult.GetRequiredValue(incident), scopeDir, parseResult.GetRequiredValue(config), loggers, logger, configuration, registry, cancellationToken);
            if (read is not { } facts)
            {
                return ExitCodes.Configuration;
            }

            var pack = DfarsReportPack.Markdown(facts.Incident, facts.Scope, facts.Obligations, DateTimeOffset.UtcNow);
            if (parseResult.GetValue(outFile) is { } file)
            {
                await File.WriteAllTextAsync(file.FullName, pack, cancellationToken);
                return ExitCodes.Ok;
            }

            await output.WriteAsync(pack);
            return ExitCodes.Ok;
        });
        return new Command("incident", "Incidents recorded in CISO Assistant and the reports they owe.") { obligations, reportPack };
    }

    /// <summary>The checked scope, its reporting obligations and the incident read from CISO Assistant; null after logging a problem.</summary>
    private static async Task<(ScopeDocument Scope, IReadOnlyList<ReportingObligation> Obligations, IncidentFacts Incident)?> ReadAsync(
        string refId, DirectoryInfo scopeDir, FileInfo configFile, ILoggerFactory loggers, ILogger logger, IConfiguration configuration, CollectorRegistry registry,
        CancellationToken cancellationToken)
    {
        var checkedScope = await RepoScope.ValidateAsync(scopeDir, null, registry, AuditSeries.Today(configuration), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
            return null;
        }

        var reporting = await RepoScope.ReportingAsync(scopeDir, cancellationToken);
        var connection = await CisoAssistantConnection.ResolveAsync(configuration, configFile, registry, cancellationToken);
        if (connection is not Result<CisoAssistantOptions>.Ok { Value: var options })
        {
            Log.Problems(logger, connection.Match(_ => [], ps => ps));
            return null;
        }

        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(options).BuildServiceProvider();
        var read = await services.GetRequiredService<IIncidentSource>().ReadAsync(refId, cancellationToken);
        if (read is not Result<IncidentFacts>.Ok { Value: var facts })
        {
            Log.Problems(logger, read.Match(_ => [], ps => ps));
            return null;
        }

        return (checkedScope.Scope, reporting.Obligations, facts);
    }

    private static async Task<int> ObligationsAsync(
        string refId, DirectoryInfo scopeDir, FileInfo configFile, bool json, TextWriter output, ILoggerFactory loggers, ILogger logger,
        IConfiguration configuration, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var reporting = await RepoScope.ReportingAsync(scopeDir, cancellationToken);
        if (reporting.Obligations.Count == 0)
        {
            return Log.Problems(logger, [new Problem("reporting.none", $"{Path.Combine(scopeDir.ToString(), "reporting.yaml")} declares no reporting obligations, so no report is owed. See docs/incidents.md.")]);
        }

        if (await ReadAsync(refId, scopeDir, configFile, loggers, logger, configuration, registry, cancellationToken) is not var (scope, obligations, facts))
        {
            return ExitCodes.Configuration;
        }

        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).ToList();
        var owed = ReportingObligations.Owed(obligations, systems, facts, DateTimeOffset.UtcNow);
        await output.WriteAsync(json ? Json(facts, owed) : Text(facts, owed));
        return ExitCodes.Ok;
    }

    internal static string Status(OwedReport report) => report.ReportedAt is not null ? "made" : report.Overdue ? "overdue" : "due";

    private static string Text(IncidentFacts incident, IReadOnlyList<OwedReport> owed)
    {
        var lines = new List<string> { $"{incident.RefId} {incident.Name}: discovered {Time(incident.Discovered)}, {owed.Count} report(s) owed" };
        lines.AddRange(owed.Select(r =>
            $"  {r.Obligation.Id}  due {Time(r.Due)}  {(r.ReportedAt is { } at ? $"made {Time(at)}" : r.Overdue ? "OVERDUE" : "due")}  to {r.Obligation.ReportTo} ({r.Why})"));
        if (owed.Any(r => r.ReportedAt is null))
        {
            lines.Add($"Record each report you make as a timeline entry on the incident: '{ReportingObligations.ReportedPrefix}<id>, <their reference>'.");
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string Json(IncidentFacts incident, IReadOnlyList<OwedReport> owed) =>
        new JsonObject
        {
            ["incident"] = incident.RefId,
            ["name"] = incident.Name,
            ["discovered"] = incident.Discovered.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["reports"] = new JsonArray([.. owed.Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Obligation.Id,
                ["name"] = r.Obligation.Name,
                ["report_to"] = r.Obligation.ReportTo,
                ["reference"] = r.Obligation.Reference,
                ["due"] = r.Due.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["status"] = Status(r),
                ["reported_at"] = r.ReportedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["why"] = r.Why,
            })]),
        }.ToJsonString(CommandJson.Escaped) + "\n";


    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
