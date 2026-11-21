using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;
using EvidenceHound.Transport.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound run</c>: for each collector, collect and store evidence, map, plan and publish; print a JSON summary.</summary>
internal static class RunCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.run");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var only = new Option<string?>("--only") { Description = "Run only the collector with this name." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Collect and store evidence and plan the updates, but send nothing to CISO Assistant." };
        var noPublish = new Option<bool>("--no-publish") { Description = "Collect and store evidence only; do not plan or publish." };

        var run = new Command("run", "Run every configured collector: collect, store evidence, map, plan and publish.") { config, only, dryRun, noPublish };
        run.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new RunOptions(parseResult.GetValue(only), parseResult.GetValue(dryRun), parseResult.GetValue(noPublish));
            var deployment = await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), cancellationToken);
            return await deployment.Match(
                d => Runner.Select(d.Collectors, options.Only).Match(
                    instances => PrepareAndRunAsync(d, instances, options, configuration, loggers, output, logger, cancellationToken),
                    problems => Task.FromResult(Usage(logger, problems))),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return run;
    }

    /// <summary>Loads the mapping and, when publishing, the transport (resolving the token); then runs.</summary>
    private static async Task<int> PrepareAndRunAsync(
        DeploymentConfig config,
        IReadOnlyList<CollectorInstance> instances,
        RunOptions options,
        IConfiguration configuration,
        ILoggerFactory loggers,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var publishing = !options.DryRun && !options.NoPublish;
        var ready = (await MappingCommands.LoadAsync(new FileInfo(config.Target.MappingPath), cancellationToken))
            .Bind(loaded => (publishing ? Transport(config, configuration, loggers) : Result.Ok(Transports.None)).Map(t => (loaded.Mapping, Transport: t)));
        return await ready.Match(
            r => RunAsync(config, instances, options, r.Mapping, r.Transport, configuration, output, logger, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> RunAsync(
        DeploymentConfig config,
        IReadOnlyList<CollectorInstance> instances,
        RunOptions options,
        FrameworkMapping mapping,
        Transports transport,
        IConfiguration configuration,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var sinks = transport;
        var step = new CollectStep(ConfigCommands.Secrets(configuration), new FileEvidenceStore(config.Evidence.Root), new SystemClock(), config.Evidence.RetentionDays);
        var runner = new Runner(
            BuiltInCollectors.Create(), step, mapping, new AuditTarget(config.Target.ComplianceAssessmentRefId, config.Target.Controls), sinks.SinkFor);

        var summary = await runner.RunAsync(instances, options, cancellationToken);
        foreach (var collector in summary.Collectors)
        {
            Log.CollectorRan(logger, collector.Collector, collector.Outcome);
            Log.Problems(logger, collector.Problems.Select(p => new Problem(p.Code, $"{collector.Collector}: {p.Message}")));
        }

        await output.WriteLineAsync(Json(summary).ToJsonString(Indented));
        return summary.Outcome == CollectorOutcome.Failed ? ExitCodes.CollectorFailed : ExitCodes.Ok;
    }

    /// <summary>The configured transport. The URL comes from the config unless <c>HOUND_CA_URL</c> overrides it; the token is a secret reference.</summary>
    private static Result<Transports> Transport(DeploymentConfig config, IConfiguration configuration, ILoggerFactory loggers)
    {
        var ca = config.CisoAssistant;
        if (ca.Transport == "kafka")
        {
            return Result.Ok(Transports.Kafka(new KafkaSinkOptions(ca.KafkaBootstrapServers!)));
        }

        var url = configuration[PublishCommand.UrlSetting] is { Length: > 0 } overridden ? new Uri(overridden.EndsWith('/') ? overridden : overridden + "/") : ca.Url;
        return ConfigCommands.Secrets(configuration).Resolve(ca.Token).Map(token => Transports.Rest(new CisoAssistantOptions(url, token.Reveal()), loggers));
    }

    private static JsonObject Json(RunSummary summary) => new()
    {
        ["outcome"] = summary.Outcome.ToString().ToLowerInvariant(),
        ["collectors"] = new JsonArray([.. summary.Collectors.Select(c => new JsonObject
        {
            ["name"] = c.Collector,
            ["outcome"] = c.Outcome.ToString().ToLowerInvariant(),
            ["problems"] = new JsonArray([.. c.Problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
            ["publish"] = c.Publish is { } report
                ? new JsonObject { ["planned"] = report.Planned, ["sent"] = report.Sent, ["skipped"] = report.Skipped, ["failed"] = report.Failed }
                : null,
        })]),
    };

    private static int Usage(ILogger logger, IReadOnlyList<Problem> problems)
    {
        foreach (var problem in problems)
        {
            Log.Usage(logger, problem.Message);
        }

        return ExitCodes.Usage;
    }

    /// <summary>Makes a sink per collector run over that run's files, and owns whatever the sinks share.</summary>
    private sealed class Transports(Func<IEvidenceFiles, IAuditUpdateSink> sinkFor, IDisposable? shared) : IDisposable
    {
        public static Transports None { get; } = new(_ => throw new InvalidOperationException("Publishing is off for this run."), null);

        public Func<IEvidenceFiles, IAuditUpdateSink> SinkFor { get; } = sinkFor;

        public static Transports Rest(CisoAssistantOptions options, ILoggerFactory loggers)
        {
            var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(options).BuildServiceProvider();
            var client = services.GetRequiredService<ICisoAssistantClient>();
            return new Transports(files => new RestSink(client, files), services);
        }

        public static Transports Kafka(KafkaSinkOptions options) => new(files => new KafkaSink(options, files), null);

        public void Dispose() => shared?.Dispose();
    }
}
