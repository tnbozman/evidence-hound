using EvidenceHound.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Scheduling;

/// <summary>A five-field cron expression in a time zone.</summary>
public sealed class CronSchedule
{
    private CronSchedule() { }

    public static Result<CronSchedule> Parse(string cron, TimeZoneInfo zone) => throw new NotImplementedException();

    public DateTimeOffset? NextAfter(DateTimeOffset instant) => throw new NotImplementedException();
}

/// <summary>A collector instance on its schedule, delayed by its jitter.</summary>
public sealed record ScheduledInstance(string Name, CronSchedule Schedule, TimeSpan Jitter);

/// <summary>What a tick starts, and which due instances it skipped because their previous run is still going.</summary>
public sealed record SchedulerTick(IReadOnlyList<string> Start, IReadOnlyList<string> Skipped);

/// <summary>Which instances are due.</summary>
public sealed class Scheduler
{
    public Scheduler(IEnumerable<ScheduledInstance> instances, DateTimeOffset now) => throw new NotImplementedException();

    public DateTimeOffset? NextDue => throw new NotImplementedException();

    public SchedulerTick TakeDue(DateTimeOffset now) => throw new NotImplementedException();

    public void Finished(string name) => throw new NotImplementedException();
}

/// <summary>A stable per-instance delay.</summary>
public static class Jitter
{
    public static TimeSpan For(string name, TimeSpan maximum) => throw new NotImplementedException();
}

/// <summary>Runs due instances and sleeps until the next.</summary>
public sealed class ServeLoop
{
    public ServeLoop(Scheduler scheduler, IClock clock, Func<TimeSpan, CancellationToken, Task> sleep, Func<string, CancellationToken, Task> run) => throw new NotImplementedException();

    public static TimeSpan MaxSleep { get; } = TimeSpan.FromMinutes(1);

    public Task RunAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}
