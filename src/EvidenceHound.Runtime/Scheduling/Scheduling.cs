using Cronos;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Scheduling;

/// <summary>A five-field cron expression read in a time zone, so "0 3 * * *" means 03:00 there, across daylight saving changes.</summary>
public sealed class CronSchedule
{
    private readonly CronExpression _expression;

    private CronSchedule(CronExpression expression, TimeZoneInfo zone)
    {
        _expression = expression;
        TimeZone = zone;
    }

    public TimeZoneInfo TimeZone { get; }

    public static Result<CronSchedule> Parse(string cron, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        try
        {
            return Result.Ok(new CronSchedule(CronExpression.Parse(cron, CronFormat.Standard), zone));
        }
        catch (CronFormatException ex)
        {
            return Result.Fail<CronSchedule>(new Problem("schedule.cron_invalid", $"'{cron}' is not a five-field cron expression: {ex.Message}"));
        }
    }

    /// <summary>The first occurrence strictly after <paramref name="instant"/>, or null when the expression never fires again.</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset instant) =>
        _expression.GetNextOccurrence(instant, TimeZone) is { } next ? next.ToUniversalTime() : null;
}

/// <summary>A collector instance on its schedule, each run delayed by the instance's jitter.</summary>
public sealed record ScheduledInstance(string Name, CronSchedule Schedule, TimeSpan Jitter);

/// <summary>What a tick starts, and which due instances it skipped because their previous run is still going.</summary>
public sealed record SchedulerTick(IReadOnlyList<string> Start, IReadOnlyList<string> Skipped);

/// <summary>
/// Which instances are due. An instance is never started while its previous run is still going: an occurrence that falls due
/// then is skipped, not queued, and occurrences missed while the host was busy or asleep run once, late, not once each.
/// Not thread-safe; the serve loop owns it.
/// </summary>
public sealed class Scheduler
{
    private readonly Dictionary<string, ScheduledInstance> _instances;
    private readonly Dictionary<string, DateTimeOffset?> _next;
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    public Scheduler(IEnumerable<ScheduledInstance> instances, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _instances = instances.ToDictionary(i => i.Name, StringComparer.Ordinal);
        _next = _instances.Values.ToDictionary(i => i.Name, i => Due(i, now), StringComparer.Ordinal);
    }

    /// <summary>The earliest time any instance falls due, or null when none ever will.</summary>
    public DateTimeOffset? NextDue => _next.Values.Where(due => due is not null).Min();

    /// <summary>The next due time of one instance.</summary>
    public DateTimeOffset? NextDueOf(string name) => _next.GetValueOrDefault(name);

    /// <summary>Starts every instance due at <paramref name="now"/> that is not running, in name order, and moves each due instance to its next occurrence.</summary>
    public SchedulerTick TakeDue(DateTimeOffset now)
    {
        var due = _next.Where(pair => pair.Value <= now).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        foreach (var name in due)
        {
            _next[name] = Due(_instances[name], now);
        }

        var start = due.Where(name => !_running.Contains(name)).ToArray();
        _running.UnionWith(start);
        return new SchedulerTick(start, [.. due.Where(_running.Contains).Except(start)]);
    }

    /// <summary>Marks an instance's run as finished, so its next occurrence can start it.</summary>
    public void Finished(string name) => _running.Remove(name);

    private static DateTimeOffset? Due(ScheduledInstance instance, DateTimeOffset after) =>
        instance.Schedule.NextAfter(after - instance.Jitter) is { } next ? next + instance.Jitter : null;
}

/// <summary>A stable delay per instance, so instances sharing a schedule do not all call their APIs at the same second.</summary>
public static class Jitter
{
    /// <summary>A delay in [0, <paramref name="maximum"/>), the same for the same name on every host and every start.</summary>
    public static TimeSpan For(string name, TimeSpan maximum)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (maximum <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // FNV-1a, because string.GetHashCode changes between processes.
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return TimeSpan.FromSeconds(hash % (ulong)Math.Max(1, (long)maximum.TotalSeconds));
    }
}

/// <summary>
/// <c>hound serve</c>'s loop: starts each due instance, one run at a time, and sleeps until the next is due (at most
/// <see cref="MaxSleep"/>, so a changed clock is noticed). A run that throws is reported and the loop carries on.
/// Stops when cancelled.
/// </summary>
public sealed class ServeLoop(
    Scheduler scheduler,
    IClock clock,
    Func<TimeSpan, CancellationToken, Task> sleep,
    Func<string, CancellationToken, Task> run,
    Action<string, Exception>? failed = null,
    Action<string>? skipped = null)
{
    public static TimeSpan MaxSleep { get; } = TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var tick = scheduler.TakeDue(clock.UtcNow);
            foreach (var name in tick.Skipped)
            {
                skipped?.Invoke(name);
            }

            foreach (var name in tick.Start)
            {
                await RunOneAsync(name, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            var wait = scheduler.NextDue is { } next ? next - clock.UtcNow : MaxSleep;
            try
            {
                await sleep(wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxSleep ? MaxSleep : wait, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunOneAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await run(name, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failed?.Invoke(name, ex);
        }
        finally
        {
            scheduler.Finished(name);
        }
    }
}
