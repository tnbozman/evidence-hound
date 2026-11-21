using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli;

/// <summary><c>hound plan</c>: work out the CISO Assistant updates for a run's check results.</summary>
internal static class PlanCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, TextWriter error)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the planned dispatcher messages as JSON instead of sending them." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var checks = new Option<FileInfo>("--checks") { Description = "Path to a check results file (JSON).", Required = true };

        var plan = new Command("plan", "Plan the CISO Assistant updates for a run's check results.") { dryRun, config, checks };
        plan.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(dryRun),
            parseResult.GetRequiredValue(config),
            parseResult.GetRequiredValue(checks),
            output,
            error,
            cancellationToken));
        return plan;
    }

    private static async Task<int> RunAsync(bool dryRun, FileInfo configFile, FileInfo checksFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (!dryRun)
        {
            await error.WriteLineAsync("plan currently supports only --dry-run; publishing arrives with the publisher (s02).");
            return ExitCodes.Usage;
        }

        var target = await LoadTargetAsync(configFile, cancellationToken);
        var mapping = await target.Map(t => new FileInfo(t.MappingPath)).Match(
            file => MappingCommands.LoadAsync(file, cancellationToken),
            problems => Task.FromResult(Result.Fail<Core.Yaml.LoadedMapping>(problems)));
        var run = await CheckResultsJson.LoadRunAsync(checksFile, cancellationToken);

        var plan = target.Bind(t => mapping.Bind(m => run.Bind(r => AuditPlanner.PlanUpdates(
            new AuditTarget(t.ComplianceAssessmentRefId, t.Controls),
            MappingEngine.ApplyMapping(m.Mapping, r.Checks),
            r.Evidence,
            r.ControlStatuses))));

        return await plan.Match(
            async updates =>
            {
                var messages = new JsonArray([.. updates.Select(u => DispatcherMessages.ToJson(u, DescribeFile))]);
                await output.WriteLineAsync(messages.ToJsonString(Indented));
                return ExitCodes.Ok;
            },
            async problems =>
            {
                foreach (var problem in problems)
                {
                    await error.WriteLineAsync($"error: {problem.Message}");
                }

                return ExitCodes.Configuration;
            });
    }

    private static string DescribeFile(Core.Checks.Evidence evidence) =>
        string.Create(CultureInfo.InvariantCulture, $"(dry run: base64 of {evidence.FileName}, {evidence.SizeBytes} bytes, sha256 {evidence.Sha256})");

    private static async Task<Result<TargetConfig>> LoadTargetAsync(FileInfo configFile, CancellationToken cancellationToken) =>
        await ConfigFile.LoadAsync(configFile.FullName, cancellationToken) switch
        {
            ConfigLoad.Loaded { Config.Target: { } target } => Result.Ok(target),
            ConfigLoad.Failed failed => Result.Fail<TargetConfig>(new Problem("config.invalid", failed.Reason)),
            _ => Result.Fail<TargetConfig>(new Problem("config.target_missing", $"{configFile} has no 'target' with a mapping and compliance_assessment.")),
        };
}
