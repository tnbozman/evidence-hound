using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// One period of an audit series: its own audit, from <see cref="Start"/> to <see cref="End"/> inclusive. A series is
/// <c>&lt;ORG&gt;-&lt;FRAMEWORK&gt;[-&lt;SYSTEM&gt;]</c>, and the period's label goes after the organisation and framework:
/// <c>EX-E8-SOPHOS-NETWORK</c> gives <c>EX-E8-Q1-2027FY-SOPHOS-NETWORK</c>.
/// </summary>
public sealed record AuditPeriod(string Series, string Label, DateOnly Start, DateOnly End)
{
    public string RefId => Series.Split('-', 3) is [var org, var framework, var system] ? $"{org}-{framework}-{Label}-{system}" : $"{Series}-{Label}";
}

/// <summary>
/// The periods of an audit that runs in a cycle. Pure. Periods follow on from the cycle's first day, one after another, and each is labelled by
/// the financial year its first day falls in, 1 July to 30 June, named by the year it ends: a quarter of that year (<c>Q1-2027FY</c> is July
/// to September 2026) or a calendar month (<c>M07-2027FY</c> is July 2026). A period is as long as the unit that labels it, so labels never repeat.
/// </summary>
public static class AuditPeriods
{
    /// <summary>The period lengths a cycle can have.</summary>
    public static IReadOnlyList<string> Lengths { get; } = ["month", "quarter"];

    /// <summary>The month a financial year starts in: July.</summary>
    public const int FinancialYearStart = 7;

    /// <summary>Whether a cycle of this length may start on this day: a month cycle on the first of any month, a quarter cycle on the first day of a financial-year quarter.</summary>
    public static bool StartsOnItsUnit(string length, DateOnly first) =>
        first.Day == 1 && (length != "quarter" || (first.Month - FinancialYearStart + 12) % 3 == 0);

    /// <summary>The period that holds <paramref name="date"/>.</summary>
    public static AuditPeriod On(string series, AuditCycle cycle, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        var first = First(cycle);
        var months = Months(cycle.Period);
        var elapsed = ((date.Year - first.Year) * 12) + date.Month - first.Month - (date.Day < first.Day ? 1 : 0);
        var index = (int)Math.Floor(elapsed / (double)months);
        var start = first.AddMonths(index * months);
        return new AuditPeriod(series, Label(cycle.Period, start), start, first.AddMonths((index + 1) * months).AddDays(-1));
    }

    /// <summary>Every period from the cycle's first up to the one that holds <paramref name="date"/>; none before the cycle starts.</summary>
    public static IReadOnlyList<AuditPeriod> Until(string series, AuditCycle cycle, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        var periods = new List<AuditPeriod>();
        for (var period = On(series, cycle, First(cycle)); period.Start <= date; period = On(series, cycle, period.End.AddDays(1)))
        {
            periods.Add(period);
        }

        return periods;
    }

    /// <summary>The audit a run on <paramref name="date"/> publishes to: the open period when the target names a series, otherwise the target itself.</summary>
    public static string Target(string auditRefId, IReadOnlyDictionary<string, AuditCycle> series, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.TryGetValue(auditRefId, out var cycle) ? On(auditRefId, cycle, date).RefId : auditRefId;
    }

    /// <summary>The cycle's first day.</summary>
    public static DateOnly First(AuditCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return DateOnly.ParseExact(cycle.Starts, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static int Months(string length) => length switch
    {
        "month" => 1,
        "quarter" => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(length), length, "A cycle's period is month or quarter."),
    };

    private static string Label(string length, DateOnly start)
    {
        var monthOfYear = (start.Month - FinancialYearStart + 12) % 12;
        var endYear = start.Month >= FinancialYearStart ? start.Year + 1 : start.Year;
        return length == "quarter"
            ? string.Create(CultureInfo.InvariantCulture, $"Q{(monthOfYear / 3) + 1}-{endYear}FY")
            : string.Create(CultureInfo.InvariantCulture, $"M{start.Month:00}-{endYear}FY");
    }
}
