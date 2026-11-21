using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound collect</c>: collect evidence from every configured collector and store it with a manifest.</summary>
internal static class CollectCommand
{
    public static Command Build(ILoggerFactory loggers, IConfiguration configuration)
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

        var collect = new Command("collect", "Collect evidence from every configured collector into the evidence store.") { dryRun, config };
        collect.SetAction(async (parseResult, cancellationToken) =>
            await (await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), cancellationToken)).Match(
                deployment => parseResult.GetValue(dryRun)
                    ? Task.FromResult(Collectors(logger, deployment))
                    : CollectAsync(deployment, configuration, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems))));
        return collect;
    }

    private static int Collectors(ILogger logger, DeploymentConfig config)
    {
        Log.CollectorsConfigured(logger, config.Collectors.Count);
        return ExitCodes.Ok;
    }

    private static async Task<int> CollectAsync(DeploymentConfig config, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        var collectors = BuiltInCollectors.Create();
        var step = new CollectStep(ConfigCommands.Secrets(configuration), new FileEvidenceStore(config.Evidence.Root), new SystemClock(), config.Evidence.RetentionDays);
        var failed = 0;
        foreach (var instance in config.Collectors)
        {
            var run = await step.RunAsync(instance, collectors[instance.Type], cancellationToken);
            failed += run.Match(
                stored =>
                {
                    Log.Collected(logger, instance.Name, stored.Manifest.Files.Count, stored.Stored.Location);
                    return 0;
                },
                problems =>
                {
                    Log.Problems(logger, problems.Select(p => new Problem(p.Code, $"{instance.Name}: {p.Message}")));
                    return 1;
                });
        }

        return failed > 0 ? ExitCodes.CollectorFailed : ExitCodes.Ok;
    }
}
