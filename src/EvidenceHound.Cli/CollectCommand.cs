using System.CommandLine;
using EvidenceHound.Runtime.Configuration;
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

        return (await ConfigCommands.LoadAsync(configFile, cancellationToken))
            .Match(config => Collectors(logger, config), problems => Log.Problems(logger, problems));
    }

    private static int Collectors(ILogger logger, DeploymentConfig config)
    {
        Log.CollectorsConfigured(logger, config.Collectors.Count);
        return ExitCodes.Ok;
    }
}
