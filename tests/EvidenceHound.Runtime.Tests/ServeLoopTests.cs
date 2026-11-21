using EvidenceHound.Core;
using EvidenceHound.Runtime.Scheduling;

namespace EvidenceHound.Runtime.Tests;

public class ServeLoopTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Runs_each_instance_when_due_and_sleeps_until_the_next()
    {
        var clock = new SteppingClock(Start);
        var runs = new List<(string Name, DateTimeOffset At)>();
        using var stop = new CancellationTokenSource();
        var scheduler = new Scheduler([new ScheduledInstance("quarter", Cron("*/15 * * * *"), TimeSpan.Zero)], Start);
        var loop = new ServeLoop(scheduler, clock, clock.SleepAsync, (name, _) =>
        {
            runs.Add((name, clock.UtcNow));
            if (runs.Count == 3)
            {
                stop.Cancel();
            }

            return Task.CompletedTask;
        });

        await loop.RunAsync(stop.Token);

        runs.ShouldBe([("quarter", Start.AddMinutes(15)), ("quarter", Start.AddMinutes(30)), ("quarter", Start.AddMinutes(45))]);
        clock.Sleeps.ShouldAllBe(sleep => sleep <= ServeLoop.MaxSleep);
    }

    [Fact]
    public async Task A_failing_run_is_logged_and_the_loop_keeps_going()
    {
        var clock = new SteppingClock(Start);
        var attempts = 0;
        using var stop = new CancellationTokenSource();
        var scheduler = new Scheduler([new ScheduledInstance("flaky", Cron("*/15 * * * *"), TimeSpan.Zero)], Start);
        var loop = new ServeLoop(scheduler, clock, clock.SleepAsync, (_, _) =>
        {
            if (++attempts == 2)
            {
                stop.Cancel();
                return Task.CompletedTask;
            }

            throw new InvalidOperationException("collector blew up");
        });

        await loop.RunAsync(stop.Token);

        attempts.ShouldBe(2);
    }

    private static CronSchedule Cron(string cron) => CronSchedule.Parse(cron, TimeZoneInfo.Utc).Match(s => s, p => throw new InvalidOperationException(p[0].Message));

    /// <summary>A fake clock whose sleeps move time forward instantly.</summary>
    private sealed class SteppingClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public List<TimeSpan> Sleeps { get; } = [];

        public Task SleepAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sleeps.Add(duration);
            UtcNow += duration;
            return Task.CompletedTask;
        }
    }
}
