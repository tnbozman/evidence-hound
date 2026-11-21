using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>One period of an audit series: its own audit, <c>&lt;series&gt;-&lt;label&gt;</c>, from <see cref="Start"/> to <see cref="End"/> inclusive.</summary>
public sealed record AuditPeriod(string Series, string Label, DateOnly Start, DateOnly End)
{
    public string RefId => $"{Series}-{Label}";
}

/// <summary>
/// The periods of an audit that runs in a cycle. Pure. Periods follow on from the cycle's first day, one after another, and each is labelled by
/// the calendar month, quarter, half or year its first day falls in (<c>2026M02</c>, <c>2026Q4</c>, <c>2027H1</c>, <c>2026</c>), or
/// <c>FY2026</c> for a year that does not start in January. A period is as long as the calendar unit that labels it, so labels never repeat.
/// </summary>
public static class AuditPeriods
{
    /// <summary>The period lengths a cycle can have.</summary>
    public static IReadOnlyList<string> Lengths { get; } = ["month", "quarter", "half_year", "year"];

    /// <summary>The period that holds <paramref name="date"/>.</summary>
    public static AuditPeriod On(string series, AuditCycle cycle, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        var first = First(cycle);
        var months = Months(cycle.Period);
        var elapsed = ((date.Year - first.Year) * 12) + date.Month - first.Month - (date.Day < first.Day ? 1 : 0);
        var index = (int)Math.Floor(elapsed / (double)months);
        var start = first.AddMonths(index * months);
        return new AuditPeriod(series, Label(cycle.Period, start, first), start, first.AddMonths((index + 1) * months).AddDays(-1));
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
        "half_year" => 6,
        "year" => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(length), length, "A cycle's period is month, quarter, half_year or year."),
    };

    private static string Label(string length, DateOnly start, DateOnly first) => length switch
    {
        "month" => string.Create(CultureInfo.InvariantCulture, $"{start.Year}M{start.Month:00}"),
        "quarter" => string.Create(CultureInfo.InvariantCulture, $"{start.Year}Q{((start.Month - 1) / 3) + 1}"),
        "half_year" => string.Create(CultureInfo.InvariantCulture, $"{start.Year}H{((start.Month - 1) / 6) + 1}"),
        _ => first.Month == 1 ? start.Year.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"FY{start.Year}"),
    };
}
