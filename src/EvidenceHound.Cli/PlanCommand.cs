using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound plan</c>: work out the CISO Assistant updates for a run's check results.</summary>
internal static class PlanCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.plan");
        var dryRun = new Option<bool>("--dry-run") { Description = "Plan offline, as if CISO Assistant held nothing, and print every dispatcher message." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var checks = new Option<FileInfo>("--checks") { Description = "Path to a check results file (JSON).", Required = true };

        var plan = new Command(
            "plan",
            $"Plan the CISO Assistant updates for a run's check results: only what differs from CISO Assistant now (reads it; needs {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting}). Sends nothing.")
        {
            dryRun, config, checks,
        };
        plan.SetAction((parseResult, cancellationToken) => parseResult.GetValue(dryRun)
            ? DryRunAsync(parseResult.GetRequiredValue(config), parseResult.GetRequiredValue(checks), registry, output, logger, cancellationToken)
            : LiveAsync(parseResult.GetRequiredValue(config), parseResult.GetRequiredValue(checks), registry, configuration, loggers, output, logger, cancellationToken));
        return plan;
    }

    private static async Task<int> LiveAsync(
        FileInfo configFile,
        FileInfo checksFile,
        CollectorRegistry registry,
        IConfiguration configuration,
        ILoggerFactory loggers,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var inputs = (await InputsAsync(configFile, checksFile, registry, cancellationToken))
            .Bind(i => PublishCommand.Connection(configuration).Map(connection => (Inputs: i, Connection: connection)));
        return await inputs.Match(
            async ready =>
            {
                using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(ready.Connection).BuildServiceProvider();
                var (targets, run) = ready.Inputs;
                var query = StateQuery.For(targets, run.Evidence, run.ControlStatuses);
                var current = await new ClientStateSource(services.GetRequiredService<ICisoAssistantClient>()).ReadAsync(query, cancellationToken);
                var runId = RunIds.From(DateTimeOffset.UtcNow);
                return await AuditPlanner.PlanChanges(targets, run.Evidence, run.ControlStatuses, current, runId).Match(
                    async changes =>
                    {
                        var json = new JsonObject
                        {
                            ["run_id"] = runId,
                            ["audits"] = RunCommand.ChangesJson(changes.Audits),
                            ["drift"] = new JsonArray([.. changes.Drift.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
                            ["messages"] = new JsonArray([.. changes.Updates.Select(u => DispatcherMessages.ToJson(u, DescribeFile))]),
                        };
                        await output.WriteLineAsync(json.ToJsonString(Indented));
                        return ExitCodes.Ok;
                    },
                    problems => Task.FromResult(Log.Problems(logger, problems)));
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> DryRunAsync(FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
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
    internal static async Task<Result<IReadOnlyList<AuditUpdate>>> PlanAsync(FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, CancellationToken cancellationToken) =>
        (await InputsAsync(configFile, checksFile, registry, cancellationToken)).Bind(i => AuditPlanner.PlanUpdates(i.Targets, i.Run.Evidence, i.Run.ControlStatuses));

    /// <summary>Each target audit with its mapped results, and the run the results came from.</summary>
    private static async Task<Result<(IReadOnlyList<TargetResults> Targets, RunResults Run)>> InputsAsync(
        FileInfo configFile, FileInfo checksFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var targets = await (await ConfigCommands.LoadAsync(configFile, registry, cancellationToken)).Match(
            config => LoadMappingsAsync(config.Targets, cancellationToken),
            problems => Task.FromResult(Result.Fail<IReadOnlyList<(TargetConfig Target, FrameworkMapping Mapping)>>(problems)));
        var run = await CheckResultsJson.LoadRunAsync(checksFile, cancellationToken);

        return targets.Bind(loaded => run.Map(r => (
            (IReadOnlyList<TargetResults>)[.. loaded.Select(t => new TargetResults(new AuditTarget(t.Target.ComplianceAssessmentRefId, t.Target.Controls), MappingEngine.ApplyMapping(t.Mapping, r.Checks)))],
            r)));
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
