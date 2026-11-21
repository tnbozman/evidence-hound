using System.CommandLine;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound config validate</c>: check a deployment config file and report every problem at once.</summary>
internal static class ConfigCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.config");
        var file = new Argument<FileInfo>("file") { Description = "Path to the deployment config file (YAML)." };
        var validate = new Command("validate", "Check a deployment config file and report every problem.") { file };
        validate.SetAction(async (parseResult, cancellationToken) =>
            await (await LoadAsync(parseResult.GetRequiredValue(file), cancellationToken)).Match(
                async _ =>
                {
                    await output.WriteLineAsync("valid");
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems))));

        return new Command("config", "Work with deployment config files.") { validate };
    }

    /// <summary>Loads a deployment config, knowing the collector types this build ships.</summary>
    internal static Task<Core.Results.Result<DeploymentConfig>> LoadAsync(FileInfo file, CancellationToken cancellationToken) =>
        ConfigLoader.LoadAsync(file.FullName, CollectorTypes.BuiltIn, cancellationToken);
}
