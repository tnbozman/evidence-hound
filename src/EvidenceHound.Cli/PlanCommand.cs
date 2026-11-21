using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound plan</c>: work out the CISO Assistant updates for a run's check results.</summary>
internal static class PlanCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, ILoggerFactory loggers, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.plan");
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the planned dispatcher messages as JSON instead of sending them." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var checks = new Option<FileInfo>("--checks") { Description = "Path to a check results file (JSON).", Required = true };

        var plan = new Command("plan", "Plan the CISO Assistant updates for a run's check results.") { dryRun, config, checks };
        plan.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(dryRun),
            parseResult.GetRequiredValue(config),
            parseResult.GetRequiredValue(checks),
            registry,
            output,
            logger,
            cancellationToken));
        return plan;
    }

    private static async Task<int> RunAsync(bool dryRun, FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        if (!dryRun)
        {
            Log.Usage(logger, "plan currently supports only --dry-run; publishing arrives with the publisher (s02).");
            return ExitCodes.Usage;
        }

        var plan = await PlanAsync(configFile, checksFile, registry, cancellationToken);

        return await plan.Match(
            async updates =>
            {
                var messages = new JsonArray([.. updates.Select(u => DispatcherMessages.ToJson(u, DescribeFile))]);
                await output.WriteLineAsync(messages.ToJsonString(Indented));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>Loads the config, its mapping and the check results, and plans the audit updates.</summary>
    internal static async Task<Result<IReadOnlyList<AuditUpdate>>> PlanAsync(FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var target = await LoadTargetAsync(configFile, registry, cancellationToken);
        var mapping = await target.Map(t => new FileInfo(t.MappingPath)).Match(
            file => MappingCommands.LoadAsync(file, cancellationToken),
            problems => Task.FromResult(Result.Fail<Core.Yaml.LoadedMapping>(problems)));
        var run = await CheckResultsJson.LoadRunAsync(checksFile, cancellationToken);

        return target.Bind(t => mapping.Bind(m => run.Bind(r => AuditPlanner.PlanUpdates(
            new AuditTarget(t.ComplianceAssessmentRefId, t.Controls),
            MappingEngine.ApplyMapping(m.Mapping, r.Checks),
            r.Evidence,
            r.ControlStatuses))));
    }

    private static string DescribeFile(Core.Checks.Evidence evidence) =>
        string.Create(CultureInfo.InvariantCulture, $"(dry run: base64 of {evidence.FileName}, {evidence.SizeBytes} bytes, sha256 {evidence.Sha256})");

    private static async Task<Result<TargetConfig>> LoadTargetAsync(FileInfo configFile, CollectorRegistry registry, CancellationToken cancellationToken) =>
        (await ConfigCommands.LoadAsync(configFile, registry, cancellationToken)).Map(config => config.Target);
}
