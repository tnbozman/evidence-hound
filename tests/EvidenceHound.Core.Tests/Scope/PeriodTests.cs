using EvidenceHound.Core.Scope;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Scope;

public sealed class PeriodTests
{
    private static readonly SourceLine At = new("audits/ex-e8-backup.yaml", 1);

    [Theory]
    [InlineData("quarter", "2026-07-01", "2026-08-15", "EX-E8-Q1-2027FY", "2026-07-01", "2026-09-30")]
    [InlineData("quarter", "2026-07-01", "2026-11-21", "EX-E8-Q2-2027FY", "2026-10-01", "2026-12-31")]
    [InlineData("quarter", "2026-07-01", "2027-02-01", "EX-E8-Q3-2027FY", "2027-01-01", "2027-03-31")]
    [InlineData("quarter", "2026-07-01", "2027-06-30", "EX-E8-Q4-2027FY", "2027-04-01", "2027-06-30")]
    [InlineData("quarter", "2026-07-01", "2027-07-01", "EX-E8-Q1-2028FY", "2027-07-01", "2027-09-30")]
    [InlineData("quarter", "2026-01-01", "2026-04-01", "EX-E8-Q4-2026FY", "2026-04-01", "2026-06-30")]
    [InlineData("month", "2026-01-01", "2026-02-28", "EX-E8-M02-2026FY", "2026-02-01", "2026-02-28")]
    [InlineData("month", "2026-01-01", "2026-07-04", "EX-E8-M07-2027FY", "2026-07-01", "2026-07-31")]
    [InlineData("month", "2026-01-01", "2026-06-30", "EX-E8-M06-2026FY", "2026-06-01", "2026-06-30")]
    public void Quarter_of_date_and_period_ref_id(string length, string starts, string date, string refId, string start, string end)
    {
        var period = AuditPeriods.On("EX-E8", new AuditCycle(length, starts, At), DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        (period.RefId, period.Start, period.End).ShouldBe((refId, DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("EX-E8-SOPHOS-NETWORK", "EX-E8-Q1-2027FY-SOPHOS-NETWORK")]
    [InlineData("EX-E8-BACKUP", "EX-E8-Q1-2027FY-BACKUP")]
    [InlineData("EX", "EX-Q1-2027FY")]
    public void System_part_follows_the_period(string series, string refId)
    {
        // s09-46: <ORG>-<FRAMEWORK>-<INTERVAL>-<YEAR>FY-<SYSTEM>: the period's label goes after the organisation and framework.
        var period = AuditPeriods.On(series, new AuditCycle("quarter", "2026-07-01", At), new DateOnly(2026, 8, 1));

        (period.Label, period.RefId).ShouldBe(("Q1-2027FY", refId));
    }

    /// <summary>Every date from the cycle's start falls in exactly one period: its own, which the next and previous periods do not overlap.</summary>
    [Property(MaxTest = 200)]
    public void Every_date_falls_in_exactly_one_period(byte length, byte startMonth, ushort offset)
    {
        // A month cycle starts in any month; a quarter cycle on a financial-year quarter (January, April, July or October).
        var quarterly = length % 2 == 1;
        var month = quarterly ? ((startMonth % 4) * 3) + 1 : (startMonth % 12) + 1;
        var cycle = new AuditCycle(quarterly ? "quarter" : "month", $"2026-{month:00}-01", At);
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
            .ShouldBe(["EX-E8-Q4-2026FY-BACKUP", "EX-E8-Q1-2027FY-BACKUP", "EX-E8-Q2-2027FY-BACKUP"]);
        AuditPeriods.Until("EX-E8-BACKUP", cycle, new DateOnly(2026, 3, 31)).ShouldBeEmpty();
    }

    [Fact]
    public void Run_publishes_to_the_open_period()
    {
        var series = new Dictionary<string, AuditCycle> { ["EX-E8"] = new("quarter", "2026-07-01", At) };

        AuditPeriods.Target("EX-E8", series, new DateOnly(2026, 12, 31)).ShouldBe("EX-E8-Q2-2027FY");
        AuditPeriods.Target("EX-E8", series, new DateOnly(2027, 1, 1)).ShouldBe("EX-E8-Q3-2027FY");
        AuditPeriods.Target("EX-E8-2026", series, new DateOnly(2027, 1, 1)).ShouldBe("EX-E8-2026");
    }

    [Fact]
    public void A_period_audit_says_the_months_it_covers()
    {
        // s15-D8: a person could not tell that Q2-2027FY was October to December 2026.
        AuditPeriods.Covers("EX-E8-Q2-2027FY").ShouldBe("October to December 2026");
        AuditPeriods.Covers("EX-E8-Q3-2027FY-BACKUP").ShouldBe("January to March 2027");
        AuditPeriods.Covers("EX-E8-M07-2027FY").ShouldBe("July 2026");
        AuditPeriods.Covers("EX-E8-M02-2027FY-BACKUP").ShouldBe("February 2027");
        AuditPeriods.Covers("EX-E8-2026").ShouldBeNull();
    }
}
