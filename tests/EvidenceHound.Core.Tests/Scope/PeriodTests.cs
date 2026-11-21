using EvidenceHound.Core.Scope;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Scope;

public sealed class PeriodTests
{
    private static readonly SourceLine At = new("audits/ex-e8-backup.yaml", 1);

    [Theory]
    [InlineData("quarter", "2026-01-01", "2026-11-21", "EX-E8-BACKUP-2026Q4", "2026-10-01", "2026-12-31")]
    [InlineData("quarter", "2026-01-01", "2026-04-01", "EX-E8-BACKUP-2026Q2", "2026-04-01", "2026-06-30")]
    [InlineData("month", "2026-01-01", "2026-02-28", "EX-E8-BACKUP-2026M02", "2026-02-01", "2026-02-28")]
    [InlineData("half_year", "2026-07-01", "2027-01-15", "EX-E8-BACKUP-2027H1", "2027-01-01", "2027-06-30")]
    [InlineData("year", "2026-07-01", "2027-03-31", "EX-E8-BACKUP-FY2026", "2026-07-01", "2027-06-30")]
    [InlineData("year", "2026-01-01", "2026-12-31", "EX-E8-BACKUP-2026", "2026-01-01", "2026-12-31")]
    public void Quarter_of_date_and_period_ref_id(string length, string starts, string date, string refId, string start, string end)
    {
        var period = AuditPeriods.On("EX-E8-BACKUP", new AuditCycle(length, starts, At), DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        (period.RefId, period.Start, period.End).ShouldBe((refId, DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Every date from the cycle's start falls in exactly one period: its own, which the next and previous periods do not overlap.</summary>
    [Property(MaxTest = 200)]
    public void Every_date_falls_in_exactly_one_period(byte length, byte startMonth, ushort offset)
    {
        string[] lengths = ["month", "quarter", "half_year", "year"];
        var cycle = new AuditCycle(lengths[length % 4], $"2026-{(startMonth % 12) + 1:00}-01", At);
        var first = DateOnly.Parse(cycle.Starts, System.Globalization.CultureInfo.InvariantCulture);
        var date = first.AddDays(offset % 2000);

        var period = AuditPeriods.On("S", cycle, date);
        var next = AuditPeriods.On("S", cycle, period.End.AddDays(1));
        var previous = date >= first.AddDays(1) && period.Start > first ? AuditPeriods.On("S", cycle, period.Start.AddDays(-1)) : null;

        date.ShouldBeInRange(period.Start, period.End);
        next.Start.ShouldBe(period.End.AddDays(1));
        next.RefId.ShouldNotBe(period.RefId);
        previous?.End.ShouldBe(period.Start.AddDays(-1));
        previous?.RefId.ShouldNotBe(period.RefId);
    }

    [Fact]
    public void Periods_until_a_date_run_from_the_first_to_the_open_one()
    {
        var cycle = new AuditCycle("quarter", "2026-04-01", At);

        AuditPeriods.Until("EX-E8-BACKUP", cycle, new DateOnly(2026, 11, 21)).Select(p => p.RefId)
            .ShouldBe(["EX-E8-BACKUP-2026Q2", "EX-E8-BACKUP-2026Q3", "EX-E8-BACKUP-2026Q4"]);
        AuditPeriods.Until("EX-E8-BACKUP", cycle, new DateOnly(2026, 3, 31)).ShouldBeEmpty();
    }

    [Fact]
    public void Run_publishes_to_the_open_period()
    {
        var series = new Dictionary<string, AuditCycle> { ["EX-E8-BACKUP"] = new("quarter", "2026-01-01", At) };

        AuditPeriods.Target("EX-E8-BACKUP", series, new DateOnly(2026, 12, 31)).ShouldBe("EX-E8-BACKUP-2026Q4");
        AuditPeriods.Target("EX-E8-BACKUP", series, new DateOnly(2027, 1, 1)).ShouldBe("EX-E8-BACKUP-2027Q1");
        AuditPeriods.Target("EX-E8-2026", series, new DateOnly(2027, 1, 1)).ShouldBe("EX-E8-2026");
    }
}
