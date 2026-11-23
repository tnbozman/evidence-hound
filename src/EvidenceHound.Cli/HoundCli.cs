using System.CommandLine;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
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

    /// <summary>Runs with <paramref name="input"/> for prompts, such as the attestation questions of <c>hound manual upload</c>.</summary>
    public static Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(args, input, output, error, configureLogging, [], new Dictionary<string, string?>(), null, cancellationToken);

    /// <summary>Runs with <paramref name="input"/> for prompts and extra configuration settings on top of the environment.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken) =>
        RunAsync(args, input, output, error, null, [], settings, null, cancellationToken);

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
        RunAsync(args, TextReader.Null, output, error, configureLogging, extraCommands, new Dictionary<string, string?>(), null, cancellationToken);

    /// <summary>Runs with extra service registrations, for example another collector.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken) =>
        RunAsync(args, TextReader.Null, output, error, configureLogging, [], new Dictionary<string, string?>(), configureServices, cancellationToken);

    /// <summary>Runs with extra configuration settings and extra service registrations, for example a collector's test doubles.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken) =>
        RunAsync(args, TextReader.Null, output, error, configureLogging, [], settings, configureServices, cancellationToken);

    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyList<Command> extraCommands,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken) =>
        RunAsync(args, TextReader.Null, output, error, configureLogging, extraCommands, settings, null, cancellationToken);

    private static async Task<int> RunAsync(
        string[] args,
        TextReader input,
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

        return await CollectorRegistry.FromServices(host.Services).Match(
            registry => InvokeAsync(args, input, output, error, extraCommands, host.Services, registry, loggers, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> InvokeAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IReadOnlyList<Command> extraCommands,
        IServiceProvider services,
        CollectorRegistry registry,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger("hound");
        var collectorServices = new CollectorServices(loggers, services.GetRequiredService<IHttpClientFactory>());
        var root = BuildRootCommand(new Terminal(input, output, error), loggers, services.GetRequiredService<IConfiguration>(), registry, collectorServices);
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

            // With --detailed-exitcode, 2 means a plan with changes, so a usage error is 1.
            return args.Contains(ScopeCommands.DetailedExitCodeFlag, StringComparer.Ordinal) ? ExitCodes.Unexpected : ExitCodes.Usage;
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

    private static RootCommand BuildRootCommand(
        Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry, CollectorServices collectorServices)
    {
        var output = terminal.Output;
        return new("Evidence Hound: collects compliance evidence and publishes it to CISO Assistant.")
        {
            CollectCommand.Build(loggers, configuration, registry, collectorServices),
            CollectorsCommand.Build(output, registry),
            ConfigCommands.Build(output, loggers, configuration, registry),
            FrameworkCommands.Build(output, loggers),
            Setup.InitCommand.Build(terminal, loggers, configuration, registry),
            Setup.AddCommands.Build(terminal, loggers, configuration, registry),
            ExplainCommand.Build(terminal, loggers, configuration),
            OnboardCommands.Build(terminal, loggers, configuration),
            ManualCommand.Build(terminal, loggers, registry, new SystemClock()),
            MappingCommands.Build(output, loggers),
            ScopeCommands.Build(output, loggers, configuration, registry),
            SkillCommands.Build(output, loggers, configuration),
            PlanCommand.Build(output, loggers, configuration, registry),
            ProfileCommands.Build(output, loggers, configuration),
            PublishCommand.Build(output, loggers, configuration, registry),
            RunCommand.Build(output, loggers, configuration, registry, collectorServices),
            ServeCommand.Build(output, loggers, configuration, registry, collectorServices),
            StatusCommand.Build(output, loggers, registry),
        };
    }

    /// <summary>One host per run. Logs go to <paramref name="error"/>; the level comes from configuration (for example <c>Logging__LogLevel__Default</c>).</summary>
    private static IHost BuildHost(
        TextWriter error,
        Action<ILoggingBuilder>? configureLogging,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Configuration.AddFileVariables();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddProvider(new StderrLoggerProvider(error));
        configureLogging?.Invoke(builder.Logging);
        builder.Services.AddHttpClient();
        builder.Services.AddBuiltInCollectors();
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}
