using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>Composition root for the <c>hound</c> command line.</summary>
public static partial class HoundCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = BuildRootCommand(output, error, configureLogging).Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError.Message);
            }

            await error.WriteLineAsync("Run 'hound --help' for usage.");
            return ExitCodes.Usage;
        }

        var invocation = new InvocationConfiguration { Output = output, Error = error };
        return await parseResult.InvokeAsync(invocation, cancellationToken);
    }

    private static RootCommand BuildRootCommand(TextWriter output, TextWriter error, Action<ILoggingBuilder>? configureLogging)
    {
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
        collect.SetAction((parseResult, cancellationToken) => CollectAsync(
            parseResult.GetValue(dryRun),
            parseResult.GetRequiredValue(config),
            error,
            configureLogging,
            cancellationToken));

        return new RootCommand("Evidence Hound: collects compliance evidence and publishes it to CISO Assistant.")
        {
            collect,
            MappingCommands.Build(output, error),
        };
    }

    private static async Task<int> CollectAsync(
        bool dryRun,
        FileInfo configFile,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        CancellationToken cancellationToken)
    {
        if (!dryRun)
        {
            await error.WriteLineAsync("collect currently supports only --dry-run; real collection arrives with the runner.");
            return ExitCodes.Usage;
        }

        using var host = BuildHost(configureLogging);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("hound.collect");

        var load = await ConfigFile.LoadAsync(configFile.FullName, cancellationToken);
        switch (load)
        {
            case ConfigLoad.Loaded loaded:
                LogCollectorsConfigured(logger, loaded.Config.CollectorNames.Count);
                return ExitCodes.Ok;
            case ConfigLoad.Failed failed:
                LogConfigFailed(logger, failed.Reason);
                return ExitCodes.Configuration;
            default:
                throw new InvalidOperationException($"Unhandled config load outcome {load.GetType().Name}.");
        }
    }

    private static IHost BuildHost(Action<ILoggingBuilder>? configureLogging)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        configureLogging?.Invoke(builder.Logging);
        return builder.Build();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} collectors configured")]
    private static partial void LogCollectorsConfigured(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Reason}")]
    private static partial void LogConfigFailed(ILogger logger, string reason);
}
