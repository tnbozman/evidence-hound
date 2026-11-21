using System.CommandLine;
using EvidenceHound.Runtime;
using Microsoft.Extensions.Configuration;
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

    /// <summary>Runs with extra configuration settings (for example <c>HOUND_CA_TOKEN</c>) on top of the environment.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, configureLogging, [], settings, cancellationToken);

    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyList<Command> extraCommands,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, configureLogging, extraCommands, new Dictionary<string, string?>(), null, cancellationToken);

    /// <summary>Runs with extra service registrations, for example another collector.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, configureLogging, [], new Dictionary<string, string?>(), configureServices, cancellationToken);

    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyList<Command> extraCommands,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, configureLogging, extraCommands, settings, null, cancellationToken);

    private static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyList<Command> extraCommands,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        using var host = BuildHost(error, configureLogging, settings, configureServices);
        var loggers = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggers.CreateLogger("hound");
        var collectorServices = new CollectorServices(loggers, host.Services.GetRequiredService<IHttpClientFactory>());

        var root = BuildRootCommand(output, loggers, host.Services.GetRequiredService<IConfiguration>(), collectorServices);
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

    private static RootCommand BuildRootCommand(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorServices collectorServices) =>
        new("Evidence Hound: collects compliance evidence and publishes it to CISO Assistant.")
        {
            CollectCommand.Build(loggers, configuration, collectorServices),
            ConfigCommands.Build(output, loggers, configuration),
            MappingCommands.Build(output, loggers),
            PlanCommand.Build(output, loggers),
            PublishCommand.Build(output, loggers, configuration),
            RunCommand.Build(output, loggers, configuration, collectorServices),
            StatusCommand.Build(output, loggers),
        };

    /// <summary>One host per run. Logs go to <paramref name="error"/>; the level comes from configuration (for example <c>Logging__LogLevel__Default</c>).</summary>
    private static IHost BuildHost(
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddProvider(new StderrLoggerProvider(error));
        configureLogging?.Invoke(builder.Logging);
        builder.Services.AddHttpClient();
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}
