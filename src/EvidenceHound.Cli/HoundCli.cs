using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// Composition root for the <c>hound</c> command line. It owns the one logging setup and the global handler:
/// expected failures come back from commands as problems, anything else is caught here, logged and turned into an exit code.
/// </summary>
public static class HoundCli
{
    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(args, output, error, configureLogging, [], cancellationToken);

    /// <summary>Runs with extra configuration settings (for example <c>HOUND_CA_TOKEN</c>) on top of the environment. Not built yet.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{args}{output}{error}{configureLogging}{settings}{cancellationToken}");

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyList<Command> extraCommands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        using var host = BuildHost(error, configureLogging);
        var loggers = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggers.CreateLogger("hound");

        var root = BuildRootCommand(output, loggers);
        foreach (var command in extraCommands)
        {
            root.Subcommands.Add(command);
        }

        var parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                Log.Usage(logger, parseError.Message);
            }

            Log.Usage(logger, "Run 'hound --help' for usage.");
            return ExitCodes.Usage;
        }

        var invocation = new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false };
        try
        {
            return await parseResult.InvokeAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log.Cancelled(logger);
            return ExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            Log.Unexpected(logger, exception, parseResult.CommandResult.Command.Name);
            Log.Details(logger, exception);

            return ExitCodes.Unexpected;
        }
    }

    private static RootCommand BuildRootCommand(TextWriter output, ILoggerFactory loggers) =>
        new("Evidence Hound: collects compliance evidence and publishes it to CISO Assistant.")
        {
            CollectCommand.Build(loggers),
            MappingCommands.Build(output, loggers),
            PlanCommand.Build(output, loggers),
        };

    /// <summary>One host per run. Logs go to <paramref name="error"/>; the level comes from configuration (for example <c>Logging__LogLevel__Default</c>).</summary>
    private static IHost BuildHost(TextWriter error, Action<ILoggingBuilder>? configureLogging)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StderrLoggerProvider(error));
        configureLogging?.Invoke(builder.Logging);
        return builder.Build();
    }
}
