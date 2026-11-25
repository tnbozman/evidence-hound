namespace EvidenceHound.Core.Scope;

/// <summary>A value a run measured for a metric instance CISO Assistant holds, by its id, with the instance's frequency and why.</summary>
public sealed record MetricMeasured(string Instance, string InstanceId, string? Frequency, double Value, string Why);

/// <summary>A sample CISO Assistant holds for the period starting at <see cref="PeriodStart"/>.</summary>
public sealed record HeldSample(DateTimeOffset PeriodStart, double? Value);

/// <summary>The sample to write for an instance's current period.</summary>
public sealed record MetricSampleWrite(string Instance, string InstanceId, DateTimeOffset PeriodStart, double Value, string Why);

/// <summary>
/// One sample per metric instance and period (s16-03). CISO Assistant keeps one value per period, so a later run in the same period replaces
/// it; a value equal to the one held (to two decimals) is not sent. Pure.
/// </summary>
public static class MetricSamples
{
    /// <summary>The sample to write, or null when CISO Assistant already holds the value for this period.</summary>
    public static MetricSampleWrite? Plan(MetricMeasured measured, IReadOnlyList<HeldSample> held, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(held);
        var period = PeriodStart(now, measured.Frequency);
        var value = Math.Round(measured.Value, 2, MidpointRounding.AwayFromZero);
        return held.Any(h => h.PeriodStart == period && h.Value is { } v && Math.Round(v, 2, MidpointRounding.AwayFromZero) == value)
            ? null
            : new MetricSampleWrite(measured.Instance, measured.InstanceId, period, value, measured.Why);
    }

    /// <summary>
    /// The start of the calendar period holding <paramref name="moment"/>, in UTC, as CISO Assistant counts it: quarter hour, hour, day, ISO
    /// week (from Monday), month, quarter or year. No frequency means daily.
    /// </summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset moment, string? frequency)
    {
        var utc = moment.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return frequency switch
        {
            "15min" => day.AddHours(utc.Hour).AddMinutes(utc.Minute - (utc.Minute % 15)),
            "hourly" => day.AddHours(utc.Hour),
            "weekly" => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            "monthly" => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
            "quarterly" => new DateTimeOffset(utc.Year, ((utc.Month - 1) / 3 * 3) + 1, 1, 0, 0, 0, TimeSpan.Zero),
            "yearly" => new DateTimeOffset(utc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ => day,
        };
    }
}
