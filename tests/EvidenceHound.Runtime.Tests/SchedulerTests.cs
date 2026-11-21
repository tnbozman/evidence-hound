using EvidenceHound.Runtime.Scheduling;

namespace EvidenceHound.Runtime.Tests;

public class SchedulerTests
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
    private static readonly string[] Names = ["a", "b", "c", "d", "e"];
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Next_occurrence_respects_cron_and_timezone()
    {
        var nightly = Schedule("0 3 * * *", Sydney);

        // 03:00 in Sydney is 16:00 UTC the day before in summer (AEDT, UTC+11) and 17:00 UTC in winter (AEST, UTC+10).
        nightly.NextAfter(Start).ShouldBe(new DateTimeOffset(2026, 11, 21, 16, 0, 0, TimeSpan.Zero));
        nightly.NextAfter(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).ShouldBe(new DateTimeOffset(2026, 7, 1, 17, 0, 0, TimeSpan.Zero));
        Schedule("0 3 * * *", TimeZoneInfo.Utc).NextAfter(Start).ShouldBe(new DateTimeOffset(2026, 11, 21, 3, 0, 0, TimeSpan.Zero));
        Schedule("*/15 * * * *", TimeZoneInfo.Utc).NextAfter(Start.AddMinutes(1)).ShouldBe(Start.AddMinutes(15));
    }

    [Fact]
    public void An_invalid_cron_expression_is_reported()
    {
        var problem = CronSchedule.Parse("61 * * * *", TimeZoneInfo.Utc).Match(_ => throw new InvalidOperationException("parsed"), p => p[0]);

        problem.Code.ShouldBe("schedule.cron_invalid");
    }

    [Fact]
    public void Does_not_overlap_same_instance()
    {
        var clock = Start;
        var scheduler = new Scheduler([Instance("m365", "*/15 * * * *")], clock);

        scheduler.TakeDue(Start.AddMinutes(15)).Start.ShouldBe(["m365"]);
        var whileRunning = scheduler.TakeDue(Start.AddMinutes(30));
        whileRunning.Start.ShouldBeEmpty();
        whileRunning.Skipped.ShouldBe(["m365"]);

        scheduler.Finished("m365");
        scheduler.TakeDue(Start.AddMinutes(44)).Start.ShouldBeEmpty();
        scheduler.TakeDue(Start.AddMinutes(45)).Start.ShouldBe(["m365"]);
    }

    [Fact]
    public void Occurrences_missed_while_running_are_skipped_not_queued()
    {
        var scheduler = new Scheduler([Instance("m365", "*/15 * * * *")], Start);
        scheduler.TakeDue(Start.AddMinutes(15));

        scheduler.Finished("m365");
        var late = scheduler.TakeDue(Start.AddMinutes(61));

        late.Start.ShouldBe(["m365"]);
        scheduler.NextDue.ShouldBe(Start.AddMinutes(75));
    }

    [Fact]
    public void Each_instance_runs_on_its_own_schedule()
    {
        var scheduler = new Scheduler([Instance("hourly", "0 * * * *"), Instance("quarter", "*/15 * * * *")], Start);

        scheduler.NextDue.ShouldBe(Start.AddMinutes(15));
        scheduler.TakeDue(Start.AddMinutes(15)).Start.ShouldBe(["quarter"]);
        scheduler.Finished("quarter");
        scheduler.TakeDue(Start.AddMinutes(60)).Start.ShouldBe(["hourly", "quarter"]);
    }

    [Fact]
    public void Jitter_delays_each_instance_by_a_stable_amount_within_the_maximum()
    {
        var maximum = TimeSpan.FromMinutes(5);
        var first = Jitter.For("example-m365", maximum);

        Jitter.For("example-m365", maximum).ShouldBe(first);
        first.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        first.ShouldBeLessThan(maximum);
        Jitter.For("example-m365", TimeSpan.Zero).ShouldBe(TimeSpan.Zero);
        Names.Select(name => Jitter.For(name, maximum)).Distinct().Count().ShouldBeGreaterThan(1);

        var scheduler = new Scheduler([new ScheduledInstance("example-m365", Schedule("0 3 * * *", TimeZoneInfo.Utc), first)], Start);
        scheduler.NextDue.ShouldBe(Start.AddHours(3) + first);
        scheduler.TakeDue(Start.AddHours(3)).Start.ShouldBeEmpty();
        scheduler.TakeDue(Start.AddHours(3) + first).Start.ShouldBe(["example-m365"]);
    }

    private static ScheduledInstance Instance(string name, string cron) => new(name, Schedule(cron, TimeZoneInfo.Utc), TimeSpan.Zero);

    private static CronSchedule Schedule(string cron, TimeZoneInfo zone) =>
        CronSchedule.Parse(cron, zone).Match(s => s, p => throw new InvalidOperationException(p[0].Message));
}
