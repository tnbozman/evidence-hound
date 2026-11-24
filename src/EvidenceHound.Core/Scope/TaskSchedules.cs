using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>A CISO Assistant task schedule: its frequency and interval, such as every 3 months as <c>MONTHLY</c>, 3.</summary>
public sealed record TaskSchedule(string Frequency, int Interval);

/// <summary>The unit of an evidence plan interval.</summary>
public enum IntervalUnit
{
    Week,
    Month,
}

/// <summary>How often a person's evidence is due: every week, or every 3, 6 or 12 months.</summary>
public sealed record EvidenceInterval(int Count, IntervalUnit Unit)
{
    /// <summary>The intervals the evidence plan accepts, as written after <c>every:</c>.</summary>
    public static IReadOnlyList<string> Allowed { get; } = ["1 week", "3 months", "6 months", "12 months"];

    /// <summary>As the plan writes it, such as "every 1 week" or "every 3 months".</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"every {Count} {(Unit == IntervalUnit.Week ? "week" : "months")}");

    /// <summary>The day one interval after <paramref name="day"/>.</summary>
    public DateOnly After(DateOnly day) => Unit == IntervalUnit.Week ? day.AddDays(7 * Count) : day.AddMonths(Count);

    /// <summary>An allowed interval from its <c>every:</c> value ("1 week") or its text ("every 1 week"); null for any other.</summary>
    public static EvidenceInterval? Parse(string? text)
    {
        var value = text?.StartsWith("every ", StringComparison.Ordinal) == true ? text["every ".Length..] : text;
        return value is null || !Allowed.Contains(value, StringComparer.Ordinal) ? null : value.Split(' ') switch
        {
            [var n, "week"] => new EvidenceInterval(int.Parse(n, CultureInfo.InvariantCulture), IntervalUnit.Week),
            [var n, _] => new EvidenceInterval(int.Parse(n, CultureInfo.InvariantCulture), IntervalUnit.Month),
            _ => null,
        };
    }
}

/// <summary>How the evidence plan's intervals are written as CISO Assistant schedules, and read back. Pure.</summary>
public static class TaskSchedules
{
    /// <summary>A week weekly; 3 or 6 months monthly with that interval; 12 months yearly, once.</summary>
    public static TaskSchedule Of(EvidenceInterval every)
    {
        ArgumentNullException.ThrowIfNull(every);
        return every switch
        {
            { Unit: IntervalUnit.Week } => new("WEEKLY", every.Count),
            { Count: 12 } => new("YEARLY", 1),
            _ => new("MONTHLY", every.Count),
        };
    }

    /// <summary>A schedule as the plan writes it, so an unchanged row compares equal; another frequency as its name and interval.</summary>
    public static string Text(TaskSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return schedule.Frequency switch
        {
            "WEEKLY" => new EvidenceInterval(schedule.Interval, IntervalUnit.Week).Text,
            "MONTHLY" => new EvidenceInterval(schedule.Interval, IntervalUnit.Month).Text,
            "YEARLY" => new EvidenceInterval(12 * schedule.Interval, IntervalUnit.Month).Text,
            var other => string.Create(CultureInfo.InvariantCulture, $"{other.ToLowerInvariant()} {schedule.Interval}"),
        };
    }
}
