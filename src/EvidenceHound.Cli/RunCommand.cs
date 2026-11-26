using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using EvidenceHound.Runtime.Storage;
using EvidenceHound.Transport.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound run</c>: for each collector, collect and store evidence, map, plan and publish; print a JSON summary.</summary>
internal static class RunCommand
{

    /// <summary>s18-04: collectors mostly wait on other systems' APIs, so a few at once shortens a run without crowding any one system.</summary>
    internal const int DefaultParallel = 4;

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry, CollectorServices collectorServices)
    {
        var logger = loggers.CreateLogger("hound.run");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var only = new Option<string?>("--only") { Description = "Run only the collector with this name." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Collect and store evidence and plan the updates, but send nothing to CISO Assistant." };
        var noPublish = new Option<bool>("--no-publish") { Description = "Collect and store evidence only; do not plan or publish." };
        var parallel = new Option<int>("--parallel")
        {
            Description = $"How many collectors collect at once (default {DefaultParallel}); each still publishes in config order.",
            DefaultValueFactory = _ => DefaultParallel,
        };
        parallel.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
            {
                result.AddError("--parallel must be at least 1.");
            }
        });

        var run = new Command("run", "Run every configured collector: collect, store evidence, map, plan and publish.") { config, only, dryRun, noPublish, parallel };
        run.SetAction((parseResult, cancellationToken) => RunConfigAsync(
            parseResult.GetRequiredValue(config),
            new RunOptions(parseResult.GetValue(only), parseResult.GetValue(dryRun), parseResult.GetValue(noPublish), parseResult.GetValue(parallel)),
            configuration,
            registry,
            collectorServices,
            output,
            logger,
            cancellationToken));
        return run;
    }

    /// <summary>Loads the config (again, so a changed compliance repo is picked up) and runs the selected collectors; returns the exit code.</summary>
    internal static async Task<int> RunConfigAsync(
        FileInfo config,
        RunOptions options,
        IConfiguration configuration,
        CollectorRegistry registry,
        CollectorServices collectorServices,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var deployment = await ConfigCommands.LoadAsync(config, registry, cancellationToken);
        return await deployment.Match(
            d => Runner.Select(d.Collectors, options.Only).Match(
                instances => PrepareAndRunAsync(d, instances, options, configuration, registry, collectorServices, output, logger, cancellationToken),
                problems => Task.FromResult(Usage(logger, problems))),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>Loads the mapping and, when publishing, the transport (resolving the token); then runs.</summary>
    private static async Task<int> PrepareAndRunAsync(
        DeploymentConfig config,
        IReadOnlyList<CollectorInstance> instances,
        RunOptions options,
        IConfiguration configuration,
        CollectorRegistry registry,
        CollectorServices collectorServices,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var publishing = !options.DryRun && !options.NoPublish;
        config = await AuditSeries.ResolveAsync(config, AuditSeries.Today(configuration), cancellationToken);
        var ready = (await PlanCommand.LoadMappingsAsync(config.Targets, cancellationToken))
            .Bind(loaded => (publishing ? Transport(config, configuration, collectorServices.Loggers) : Result.Ok(Transports.None)).Map(t => (Targets: loaded, Transport: t)));
        return await ready.Match(
            r => RunAsync(
                config,
                instances,
                options,
                [.. r.Targets.Select(t => new RunTarget(t.Mapping, new AuditTarget(t.Target.ComplianceAssessmentRefId, t.Target.Controls) { NoEvidence = t.Target.NoEvidence }, t.Target.HealthControlRefId))],
                r.Transport,
                configuration,
                registry,
                collectorServices,
                output,
                logger,
                cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> RunAsync(
        DeploymentConfig config,
        IReadOnlyList<CollectorInstance> instances,
        RunOptions options,
        IReadOnlyList<RunTarget> targets,
        Transports transport,
        IConfiguration configuration,
        CollectorRegistry registry,
        CollectorServices collectorServices,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var sinks = transport;
        var step = await AuditSeries.CollectStepAsync(config, configuration, collectorServices, cancellationToken);
        var latest = new FileLatestResults(config.Evidence.Root, [.. config.Collectors.Select(c => c.Name)]);
        var runner = new Runner(
            registry.ByType,
            step,
            targets,
            sinks.SinkFor,
            new SystemClock(),
            new FileRunRecordStore(config.Evidence.Root))
        {
            States = sinks.States,
            History = new FileRunHistory(config.Evidence.Root),
            Latest = latest,
        };

        var summary = await runner.RunAsync(instances, options, cancellationToken);
        var recovery = sinks.Client is { } client && config.Scope is { } scopeDir && !options.DryRun && !options.NoPublish
            ? await RecoveryAsync(client, scopeDir, latest, cancellationToken)
            : RecoveryReport.None;
        var metrics = sinks.Client is { } metricClient && config.Scope is { } metricScope && !options.DryRun && !options.NoPublish
            ? await MetricsAsync(metricClient, metricScope, latest, summary, Now(configuration), cancellationToken)
            : MetricReport.None;
        foreach (var problem in recovery.Problems.Concat(metrics.Problems))
        {
            Log.Warning(logger, $"{problem.Message} ({problem.Code})");
        }

        foreach (var collector in summary.Collectors)
        {
            Log.CollectorRan(logger, collector.Collector, collector.Outcome);
            Log.Problems(logger, collector.Problems.Select(p => new Problem(p.Code, $"{collector.Collector}: {p.Message}")));
        }

        IReadOnlyList<Problem> warnings = options.DryRun ? [DryRunStateNotRead] : [];
        foreach (var warning in warnings)
        {
            Log.Warning(logger, warning.Message);
        }

        var json = Json(summary, warnings);
        json["recovery"] = new JsonArray([.. recovery.Set.Select(r => new JsonObject
        {
            ["assessment"] = r.AssessmentKey,
            ["field"] = r.Field,
            ["value"] = r.Value,
            ["why"] = r.Why,
        })]);
        json["metrics"] = new JsonArray([.. metrics.Recorded.Select(m => new JsonObject
        {
            ["instance"] = m.Instance,
            ["period_start"] = m.PeriodStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            ["value"] = m.Value,
            ["why"] = m.Why,
        })]);
        await output.WriteLineAsync(json.ToJsonString(CommandJson.Escaped));
        return summary.Outcome == CollectorOutcome.Failed ? ExitCodes.CollectorFailed : ExitCodes.Ok;
    }

    /// <summary>
    /// s15-05: the BIA recovery flags named by checks, set from every collector's latest results. The BIA files are read on their own, as
    /// <c>hound scope plan</c> has already checked them.
    /// </summary>
    private static async Task<RecoveryReport> RecoveryAsync(ICisoAssistantClient client, string scopeDir, FileLatestResults latest, CancellationToken cancellationToken)
    {
        var biaDir = Path.Combine(scopeDir, "bia");
        if (!Directory.Exists(biaDir))
        {
            return RecoveryReport.None;
        }

        var sources = await Task.WhenAll(Directory.GetFiles(biaDir, "*.yaml").Order(StringComparer.Ordinal)
            .Select(async path => new ScopeSource(path, await File.ReadAllTextAsync(path, cancellationToken))));
        var bias = ScopeLoader.Parse(new ScopeSource(Path.Combine(scopeDir, "setup.yaml"), "organisation:\n  root_folder: Global\n"), null, [], bias: sources).Scope.Bias;
        var results = (await latest.OthersAsync("", cancellationToken)).SelectMany(l => l.Results.Checks).ToList();
        return await RecoveryPublisher.PublishAsync(client, bias, results, cancellationToken);
    }

    /// <summary>
    /// s16-03: the metric instances with <c>source: hound</c>, measured from every collector's latest results and this run's collectors, each
    /// recorded for its current period. <c>metrics.yaml</c> is read on its own, as <c>hound scope plan</c> has already checked it.
    /// </summary>
    private static async Task<MetricReport> MetricsAsync(ICisoAssistantClient client, string scopeDir, FileLatestResults latest, RunSummary summary, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var path = Path.Combine(scopeDir, "metrics.yaml");
        if (!File.Exists(path))
        {
            return MetricReport.None;
        }

        var metrics = ScopeLoader.Parse(
            new ScopeSource(Path.Combine(scopeDir, "setup.yaml"), "organisation:\n  root_folder: Global\n"), null, [], metrics: new ScopeSource(path, await File.ReadAllTextAsync(path, cancellationToken))).Scope.Metrics;
        if (!metrics.Instances.Any(i => i.Source is not null))
        {
            return MetricReport.None;
        }

        var results = (await latest.OthersAsync("", cancellationToken)).SelectMany(l => l.Results.Checks).ToList();
        var run = new RunMeasures(results, summary.Collectors.Count, summary.Collectors.Count(c => c.Outcome == CollectorOutcome.Succeeded), now);
        return await MetricPublisher.PublishAsync(client, metrics.Instances, run, cancellationToken);
    }

    /// <summary>When a run measures: now, or the same time of day on <c>HOUND_TODAY</c> when it names an earlier day.</summary>
    private static DateTimeOffset Now(IConfiguration configuration)
    {
        var now = DateTimeOffset.UtcNow;
        var today = AuditSeries.Today(configuration);
        var moved = new DateTimeOffset(today.ToDateTime(TimeOnly.FromTimeSpan(now.TimeOfDay)), TimeSpan.Zero);
        return moved < now ? moved : now;
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
        return ConfigCommands.Secrets(configuration).Resolve(ca.Token).Map(token => Transports.Rest(new CisoAssistantOptions(url, token.Reveal()), loggers, AuditSeries.Today(configuration)));
    }

    /// <summary>A dry run sends nothing and reads nothing, so its plan lists every update as if CISO Assistant held nothing.</summary>
    private static readonly Problem DryRunStateNotRead = new(
        "run.dry_run_state_not_read",
        "Dry run: CISO Assistant was not read, so each plan lists every update as if CISO Assistant held nothing. "
        + "It does not show that CISO Assistant is reachable or that the token works; `hound scope plan` reads CISO Assistant and shows both.");

    private static JsonObject Json(RunSummary summary, IReadOnlyList<Problem> warnings) => new()
    {
        ["outcome"] = summary.Outcome.ToString().ToLowerInvariant(),
        ["run_id"] = summary.RunId,
        ["warnings"] = new JsonArray([.. warnings.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
        ["collectors"] = new JsonArray([.. summary.Collectors.Select(c => new JsonObject
        {
            ["name"] = c.Collector,
            ["outcome"] = c.Outcome.ToString().ToLowerInvariant(),
            ["problems"] = new JsonArray([.. c.Problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
            ["publish"] = c.Publish is { } report
                ? new JsonObject
                {
                    ["planned"] = report.Planned,
                    ["sent"] = report.Sent,
                    ["skipped"] = report.Skipped,
                    ["failed"] = report.Failed,
                    ["audits"] = PublishCommand.AuditsJson(report),
                }
                : null,
            ["changes"] = ChangesJson(c.Changes),
            ["drift"] = new JsonArray([.. c.Drift.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
        })]),
    };

    internal static JsonArray ChangesJson(IReadOnlyList<AuditChanges> changes) =>
        new([.. changes.Select(a => new JsonObject
        {
            ["compliance_assessment"] = a.ComplianceAssessmentRefId,
            ["changed"] = a.Changed,
            ["unchanged"] = a.Unchanged,
            ["drift"] = a.Drift,
        })]);

    private static int Usage(ILogger logger, IReadOnlyList<Problem> problems)
    {
        foreach (var problem in problems)
        {
            Log.Usage(logger, problem.Message);
        }

        return ExitCodes.Usage;
    }

    /// <summary>Makes a sink per collector run over that run's files, and owns whatever the sinks share.</summary>
    private sealed class Transports(Func<IEvidenceFiles, IAuditUpdateSink> sinkFor, IDisposable? shared, ICurrentStateSource? states = null, ICisoAssistantClient? client = null) : IDisposable
    {
        /// <summary>The REST client, for what a run writes besides the audits (s15-05); none over Kafka.</summary>
        public ICisoAssistantClient? Client { get; } = client;

        public static Transports None { get; } = new(_ => throw new InvalidOperationException("Publishing is off for this run."), null);

        public Func<IEvidenceFiles, IAuditUpdateSink> SinkFor { get; } = sinkFor;

        /// <summary>CISO Assistant's current state over REST; the Kafka dispatcher cannot read, so with it everything is sent.</summary>
        public ICurrentStateSource States { get; } = states ?? NoCurrentState.Instance;

        public static Transports Rest(CisoAssistantOptions options, ILoggerFactory loggers, DateOnly today)
        {
            var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(options).BuildServiceProvider();
            var client = services.GetRequiredService<ICisoAssistantClient>();
            return new Transports(files => new RestSink(client, files), services, new ClientStateSource(client, today), client);
        }

        public static Transports Kafka(KafkaSinkOptions options) => new(files => new KafkaSink(options, files), null);

        public void Dispose() => shared?.Dispose();
    }
}
