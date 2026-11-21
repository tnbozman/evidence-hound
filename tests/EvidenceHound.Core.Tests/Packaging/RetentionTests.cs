using EvidenceHound.Core.Packaging;

namespace EvidenceHound.Core.Tests.Packaging;

public class RetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private static StoredRun Run(int daysAgo, bool complete = true) => new("idp", Now.AddDays(-daysAgo), $"run-{daysAgo}", complete);

    [Fact]
    public void Keeps_latest_successful_run()
    {
        var runs = new[] { Run(100), Run(60), Run(40, complete: false) };

        Retention.RunsToDelete(runs, Now, days: 30).ShouldBe([Run(100), Run(40, complete: false)]);
    }

    [Fact]
    public void Deletes_only_runs_older_than_the_retention()
    {
        var runs = new[] { Run(45), Run(31), Run(29), Run(1) };

        Retention.RunsToDelete(runs, Now, days: 30).ShouldBe([Run(45), Run(31)]);
    }

    [Fact]
    public void A_run_exactly_at_the_limit_is_kept()
    {
        Retention.RunsToDelete([Run(30), Run(1)], Now, days: 30).ShouldBeEmpty();
    }

    [Fact]
    public void An_incomplete_newer_run_does_not_protect_older_ones_but_the_latest_complete_is_kept()
    {
        var runs = new[] { Run(90), Run(80), Run(1, complete: false) };

        Retention.RunsToDelete(runs, Now, days: 30).ShouldBe([Run(90)]);
    }

    [Fact]
    public void Closed_period_final_run_is_kept()
    {
        var runs = new[] { Run(100), Run(60), Run(40), Run(10) };
        var periodEnd = DateOnly.FromDateTime(Now.AddDays(-50).UtcDateTime);

        Retention.RunsToDelete(runs, Now, days: 30, [periodEnd], periodRetentionYears: 7).ShouldBe([Run(100), Run(40)]);
    }

    [Fact]
    public void Period_final_run_older_than_the_period_retention_goes()
    {
        var old = new StoredRun("idp", Now.AddYears(-8), "run-old", Complete: true);
        var periodEnd = DateOnly.FromDateTime(Now.AddYears(-8).AddDays(1).UtcDateTime);

        Retention.RunsToDelete([old, Run(1)], Now, days: 30, [periodEnd], periodRetentionYears: 7).ShouldBe([old]);
    }
}
