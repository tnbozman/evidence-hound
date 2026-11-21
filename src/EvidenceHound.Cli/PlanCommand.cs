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

    /// <summary>Loads the config, its targets' mappings and the check results, and plans the updates of every target audit.</summary>
    internal static async Task<Result<IReadOnlyList<AuditUpdate>>> PlanAsync(FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var targets = await (await ConfigCommands.LoadAsync(configFile, registry, cancellationToken)).Match(
            config => LoadMappingsAsync(config.Targets, cancellationToken),
            problems => Task.FromResult(Result.Fail<IReadOnlyList<(TargetConfig Target, FrameworkMapping Mapping)>>(problems)));
        var run = await CheckResultsJson.LoadRunAsync(checksFile, cancellationToken);

        return targets.Bind(loaded => run.Bind(r => AuditPlanner.PlanUpdates(
            [.. loaded.Select(t => new TargetResults(new AuditTarget(t.Target.ComplianceAssessmentRefId, t.Target.Controls), MappingEngine.ApplyMapping(t.Mapping, r.Checks)))],
            r.Evidence,
            r.ControlStatuses)));
    }

    /// <summary>Each target with its mapping, loaded from the mapping file it names.</summary>
    internal static async Task<Result<IReadOnlyList<(TargetConfig Target, FrameworkMapping Mapping)>>> LoadMappingsAsync(IReadOnlyList<TargetConfig> targets, CancellationToken cancellationToken)
    {
        var loaded = new List<Result<(TargetConfig Target, FrameworkMapping Mapping)>>(targets.Count);
        foreach (var target in targets)
        {
            loaded.Add((await MappingCommands.LoadAsync(new FileInfo(target.MappingPath), cancellationToken)).Map(m => (target, m.Mapping)));
        }

        return Result.Combine(loaded);
    }

    private static string DescribeFile(Core.Checks.Evidence evidence) =>
        string.Create(CultureInfo.InvariantCulture, $"(dry run: base64 of {evidence.FileName}, {evidence.SizeBytes} bytes, sha256 {evidence.Sha256})");

}
