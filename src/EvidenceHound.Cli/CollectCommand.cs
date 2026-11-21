using System.CommandLine;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound collect</c>: collect evidence from every configured collector.</summary>
internal static class CollectCommand
{
    public static Command Build(ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.collect");
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Load the config and report what would run, without contacting any system.",
        };
        var config = new Option<FileInfo>("--config")
        {
            Description = "Path to the deployment config file (YAML).",
            Required = true,
        };

        var collect = new Command("collect", "Collect evidence from every configured collector.") { dryRun, config };
        collect.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(dryRun),
            parseResult.GetRequiredValue(config),
            logger,
            cancellationToken));
        return collect;
    }

    private static async Task<int> RunAsync(bool dryRun, FileInfo configFile, ILogger logger, CancellationToken cancellationToken)
    {
        if (!dryRun)
        {
            Log.Usage(logger, "collect currently supports only --dry-run; real collection arrives with the runner.");
            return ExitCodes.Usage;
        }

        return await ConfigFile.LoadAsync(configFile.FullName, cancellationToken) switch
        {
            ConfigLoad.Loaded loaded => Collectors(logger, loaded.Config),
            ConfigLoad.Failed failed => Log.Problems(logger, [new Problem("config.invalid", failed.Reason)]),
            var other => throw new InvalidOperationException($"Unhandled config load outcome {other.GetType().Name}."),
        };
    }

    private static int Collectors(ILogger logger, HoundConfig config)
    {
        Log.CollectorsConfigured(logger, config.CollectorNames.Count);
        return ExitCodes.Ok;
    }
}
