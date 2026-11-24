using System.Globalization;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Reporting;

/// <summary>Whether a reporting period is a calendar month (the committee report) or a calendar quarter (the board report).</summary>
public enum PeriodKind
{
    Month,
    Quarter,
}

/// <summary>s12-04: a reporting period, written <c>2026-11</c> for a month and <c>2026-Q4</c> for a quarter.</summary>
public sealed partial record ReportingPeriod(PeriodKind Kind, int Year, int Number)
{
    /// <summary>The first day of the period.</summary>
    public DateOnly Start => new(Year, Kind == PeriodKind.Month ? Number : ((Number - 1) * 3) + 1, 1);

    /// <summary>The last day of the period.</summary>
    public DateOnly End => Start.AddMonths(Kind == PeriodKind.Month ? 1 : 3).AddDays(-1);

    /// <summary>The period before this one, of the same kind.</summary>
    public ReportingPeriod Previous() => Kind == PeriodKind.Month
        ? Number == 1 ? this with { Year = Year - 1, Number = 12 } : this with { Number = Number - 1 }
        : Number == 1 ? this with { Year = Year - 1, Number = 4 } : this with { Number = Number - 1 };

    /// <summary>Whether <paramref name="date"/> falls in the period.</summary>
    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public override string ToString() => Kind == PeriodKind.Month
        ? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Number:D2}")
        : string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-Q{Number}");

    public static Result<ReportingPeriod> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = Pattern().Match(text.Trim());
        if (!match.Success)
        {
            return Result.Fail<ReportingPeriod>(new Problem("report.period_invalid", $"'{text}' is not a period: use YYYY-MM for a month or YYYY-Qn for a quarter."));
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        return match.Groups["quarter"].Success
            ? Result.Ok(new ReportingPeriod(PeriodKind.Quarter, year, int.Parse(match.Groups["quarter"].Value, CultureInfo.InvariantCulture)))
            : Result.Ok(new ReportingPeriod(PeriodKind.Month, year, int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture)));
    }

    [GeneratedRegex(@"^(?<year>\d{4})-(?:(?<month>0[1-9]|1[0-2])|Q(?<quarter>[1-4]))$")]
    private static partial Regex Pattern();
}
