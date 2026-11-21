using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound serve</c>: runs each collector on its cron schedule, in the config's time zone and with its jitter, never two runs
/// of one collector at once, until stopped. Each run is <c>hound run --only &lt;name&gt;</c>, reading the config afresh.
/// </summary>
internal static class ServeCommand
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry, CollectorServices collectorServices)
    {
        var logger = loggers.CreateLogger("hound.serve");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var serve = new Command("serve", "Run each collector on its schedule until stopped (the runner container's default). Schedule changes need a restart.") { config };
        serve.SetAction(async (parseResult, cancellationToken) =>
        {
            var file = parseResult.GetRequiredValue(config);
            var deployment = await ConfigCommands.LoadAsync(file, registry, cancellationToken);
            return await deployment.Bind(Schedule).Match(
                async scheduler =>
                {
                    await ServeAsync(scheduler.Scheduler, scheduler.Config, file, configuration, registry, collectorServices, output, logger, cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return serve;
    }

    private static Result<(Scheduler Scheduler, DeploymentConfig Config)> Schedule(DeploymentConfig config) =>
        Result.Combine(config.Collectors.Select(c => CronSchedule.Parse(c.Schedule, config.Scheduler.TimeZone).Match(
                schedule => Result.Ok(new ScheduledInstance(c.Name, schedule, Jitter.For(c.Name, config.Scheduler.MaxJitter))),
                problems => Result.Fail<ScheduledInstance>(problems.Select(p => new Problem(p.Code, $"collector {c.Name}: {p.Message}"))))))
            .Map(instances => (new Scheduler(instances, new SystemClock().UtcNow), config));

    private static async Task ServeAsync(
        Scheduler scheduler,
        DeploymentConfig config,
        FileInfo file,
        IConfiguration configuration,
        CollectorRegistry registry,
        CollectorServices collectorServices,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var zone = config.Scheduler.TimeZone;
        foreach (var collector in config.Collectors)
        {
            var next = new NextRun(scheduler, collector.Name, zone);
            Log.NextRun(logger, collector.Name, next);
        }

        var loop = new ServeLoop(
            scheduler,
            new SystemClock(),
            Task.Delay,
            async (name, token) =>
            {
                var exitCode = await RunCommand.RunConfigAsync(file, new RunOptions(name), configuration, registry, collectorServices, output, logger, token);
                var next = new NextRun(scheduler, name, zone);
                Log.ScheduledRunFinished(logger, name, exitCode, next);
            },
            (name, ex) => Log.ScheduledRunFailed(logger, ex, name),
            name => Log.ScheduledRunSkipped(logger, name));
        await loop.RunAsync(cancellationToken);
        Log.Stopped(logger);
    }

}

/// <summary>When a collector runs next, shown in the schedule's time zone and in UTC; read and formatted only when logged.</summary>
internal sealed class NextRun(Scheduler scheduler, string collector, TimeZoneInfo zone)
{
    public override string ToString() => scheduler.NextDueOf(collector) is { } at
        ? string.Create(CultureInfo.InvariantCulture, $"{TimeZoneInfo.ConvertTime(at, zone):yyyy-MM-dd HH:mm:ss} {zone.Id} ({at.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC)")
        : "never";
}
