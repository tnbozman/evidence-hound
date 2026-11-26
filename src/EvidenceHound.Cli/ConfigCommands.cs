using System.CommandLine;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound config validate</c>: check a deployment config file and report every problem at once.</summary>
internal static class ConfigCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.config");
        var file = new Argument<FileInfo>("file") { Description = "Path to the deployment config file (YAML)." };
        var checkSecrets = new Option<bool>("--check-secrets") { Description = "Also resolve every secret reference, without printing any secret." };
        var validate = new Command("validate", "Check a deployment config file and report every problem.") { file, checkSecrets };
        validate.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = await DeploymentFile.LoadAsync(parseResult.GetRequiredValue(file), registry, cancellationToken);
            var checkedConfig = parseResult.GetValue(checkSecrets) ? config.Bind(c => WithSecrets(c, configuration)) : config;
            return await checkedConfig.Match(
                async loaded =>
                {
                    WarnAboutInstances(loaded, registry, logger);
                    WarnIfEvidenceRootNotWritable(loaded, file: parseResult.GetRequiredValue(file), logger);
                    await output.WriteLineAsync("valid");
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        return new Command("config", "Work with deployment config files.") { validate };
    }

    /// <summary>
    /// Logs a warning for each problem a collector finds with its own instances (such as a rest instance whose profile is not valid).
    /// Such an instance stops only itself, so this never fails the command.
    /// </summary>
    internal static void WarnAboutInstances(DeploymentConfig config, CollectorRegistry registry, ILogger logger)
    {
        foreach (var instance in config.Collectors)
        {
            if (registry.ByType.GetValueOrDefault(instance.Type) is IInstanceValidator validator
                && validator.ValidateInstance(new CollectorSettings(instance.Settings), [.. instance.Secrets.Keys]) is { Count: > 0 } problems)
            {
                Log.InstanceProblems(logger, instance.Name, problems);
            }
        }
    }

    /// <summary>
    /// Warns when this user cannot write the evidence root, or create it. Only a warning: the runner container owns the default root, and a
    /// repo's CI validates the config where nobody can write it. Nothing is left behind: the probe file is deleted.
    /// </summary>
    internal static void WarnIfEvidenceRootNotWritable(DeploymentConfig config, FileInfo file, ILogger logger)
    {
        var root = Path.GetFullPath(config.Evidence.Root, file.DirectoryName ?? Directory.GetCurrentDirectory());
        if (!Writable(root))
        {
            Log.Warning(logger, $"evidence.root {root} is not writable by this user, so 'hound run' here will fail. That is expected where only "
                + "the runner container writes it; to run hound yourself, set evidence.root in hound.yaml to a folder you can write, or create it "
                + "for your user: sudo mkdir -p <folder> && sudo chown $USER <folder>.");
        }
    }

    private static bool Writable(string root)
    {
        var existing = root;
        while (!Directory.Exists(existing))
        {
            if (File.Exists(existing) || Path.GetDirectoryName(existing) is not { } parent)
            {
                return false;
            }

            existing = parent;
        }

        var probe = Path.Combine(existing, $".hound-write-check-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static Result<DeploymentConfig> WithSecrets(DeploymentConfig config, IConfiguration configuration) =>
        SecretCheck.Missing(config, DeploymentFile.Secrets(configuration)) is { Count: > 0 } missing ? Result.Fail<DeploymentConfig>(missing) : Result.Ok(config);
}
