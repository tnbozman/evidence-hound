using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s16-03: one sample per instance and period, replaced by a later run in the period, and not sent when unchanged.</summary>
public class MetricPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset May = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static MetricMeasured Measured(double value) => new("EX-MET-PASS-CORP", "mi-pass", "monthly", value, "2 of 3 checks passed.");

    [Fact]
    public void Same_period_value_replaces_and_unchanged_is_not_sent()
    {
        var replaced = MetricSamples.Plan(Measured(90), [new HeldSample(June, 80), new HeldSample(May, 70)], Now).ShouldNotBeNull();
        (replaced.InstanceId, replaced.PeriodStart, replaced.Value).ShouldBe(("mi-pass", June, 90));
        replaced.Why.ShouldBe("2 of 3 checks passed.");

        MetricSamples.Plan(Measured(80), [new HeldSample(June, 80)], Now).ShouldBeNull();
        MetricSamples.Plan(Measured(80.004), [new HeldSample(June, 80)], Now).ShouldBeNull();
        MetricSamples.Plan(Measured(80), [new HeldSample(May, 80)], Now).ShouldNotBeNull().PeriodStart.ShouldBe(June);
        MetricSamples.Plan(Measured(100.0 / 3), [], Now).ShouldNotBeNull().Value.ShouldBe(33.33);
    }

    [Theory]
    [InlineData("15min", "2026-06-20T01:45:00Z")]
    [InlineData("hourly", "2026-06-20T01:00:00Z")]
    [InlineData("daily", "2026-06-20T00:00:00Z")]
    [InlineData(null, "2026-06-20T00:00:00Z")]
    [InlineData("weekly", "2026-06-15T00:00:00Z")]
    [InlineData("monthly", "2026-06-01T00:00:00Z")]
    [InlineData("quarterly", "2026-04-01T00:00:00Z")]
    [InlineData("yearly", "2026-01-01T00:00:00Z")]
    public void A_period_starts_as_ciso_assistant_counts_it(string? frequency, string start)
    {
        MetricSamples.PeriodStart(new DateTimeOffset(2026, 6, 20, 11, 52, 7, TimeSpan.FromHours(10)), frequency).ShouldBe(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
    }
}
