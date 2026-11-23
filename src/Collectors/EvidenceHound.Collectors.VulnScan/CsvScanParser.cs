using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// Pure: reads a CSV scan export through a <see cref="ColumnMap"/>. Each row is one finding. Severity is a name (info, low, medium,
/// high, critical; moderate and informational too) or 0 to 4. A yes/no column is no when empty or one of <see cref="No"/>, and yes
/// for anything else, so Defender's <c>ExploitabilityLevel</c> reads as exploit available unless it is <c>NoExploit</c>. The scan
/// date is the latest value in the scan date column, or when the file was written when there is none.
/// </summary>
public static class CsvScanParser
{
    /// <summary>The values a yes/no column reads as no, compared ignoring case.</summary>
    public static IReadOnlyList<string> No { get; } = ["false", "no", "n", "0", "none", "noexploit", "not available"];

    private static readonly char[] CveSeparators = [',', ';', ' '];

    public static Result<ScanRun> Parse(string fileName, string csv, ColumnMap map, DateTimeOffset written)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(map);
        var rows = CsvRows.Read(csv.TrimStart('﻿')).Where(r => r.Cells is not [""]).ToArray();
        if (rows.Length == 0)
        {
            return Result.Fail<ScanRun>(ScanProblems.Invalid(fileName, 1, "the export is empty; it needs a header row."));
        }

        var header = rows[0].Cells.Select(c => c.Trim()).ToArray();
        var missing = map.Mapped().Where(m => !header.Contains(m.Column, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0)
        {
            return Result.Fail<ScanRun>(missing.Select(m => ScanProblems.Invalid(fileName, rows[0].Line, $"no column '{m.Column}' (setting {m.Setting}).")));
        }

        var index = map.Mapped().DistinctBy(m => m.Column, StringComparer.Ordinal).ToDictionary(m => m.Column, m => Array.FindIndex(header, h => string.Equals(h, m.Column, StringComparison.OrdinalIgnoreCase)), StringComparer.Ordinal);
        var parsed = rows.Skip(1).Select(row => Row(fileName, row, header.Length, map, index)).ToArray();
        return Result.Combine(parsed).Map(found =>
        {
            var findings = found.Select(f => f.Finding).ToArray();
            var dates = found.Select(f => f.ScanDate).OfType<DateTimeOffset>().ToArray();
            return new ScanRun(fileName, dates.Length > 0 ? dates.Max() : written, [.. findings.Select(f => f.Host).Distinct(StringComparer.Ordinal)], findings);
        });
    }

    private static Result<(ScanFinding Finding, DateTimeOffset? ScanDate)> Row(string fileName, CsvRow row, int width, ColumnMap map, Dictionary<string, int> index)
    {
        if (row.Cells.Count != width)
        {
            return Result.Fail<(ScanFinding, DateTimeOffset?)>(ScanProblems.Invalid(fileName, row.Line, string.Create(CultureInfo.InvariantCulture, $"{row.Cells.Count} cells, but the header has {width}.")));
        }

        string Cell(string? column) => column is null ? string.Empty : row.Cells[index[column]].Trim();
        var problems = new List<string>();
        var host = Required(Cell(map.Host), "host", problems);
        var id = Required(Cell(map.FindingId), "finding ID", problems);
        var severity = ParseSeverity(Cell(map.Severity), problems);
        var firstSeen = Date(Cell(map.FirstSeen), "first seen", problems);
        var published = Date(Cell(map.PatchPublished), "patch published", problems);
        var scanDate = Date(Cell(map.ScanDate), "scan date", problems);
        if (problems.Count > 0)
        {
            return Result.Fail<(ScanFinding, DateTimeOffset?)>(problems.Select(p => ScanProblems.Invalid(fileName, row.Line, p)));
        }

        var finding = new ScanFinding(
            host,
            id,
            Cell(map.Name) is { Length: > 0 } name ? name : id,
            severity,
            [.. Cell(map.Cve).Split(CveSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            firstSeen,
            published,
            Yes(Cell(map.ExploitAvailable)),
            Yes(Cell(map.Unsupported)));
        return Result.Ok((finding, scanDate));
    }

    private static string Required(string value, string field, List<string> problems)
    {
        if (value.Length == 0)
        {
            problems.Add($"{field} is empty.");
        }

        return value;
    }

    private static Severity ParseSeverity(string text, List<string> problems)
    {
        switch (text.ToUpperInvariant())
        {
            case "0" or "INFO" or "INFORMATIONAL" or "NONE":
                return Severity.Info;
            case "1" or "LOW":
                return Severity.Low;
            case "2" or "MEDIUM" or "MODERATE":
                return Severity.Medium;
            case "3" or "HIGH":
                return Severity.High;
            case "4" or "CRITICAL":
                return Severity.Critical;
            default:
                problems.Add($"severity '{text}' is not one of info, low, medium, high, critical (or 0 to 4).");
                return Severity.Info;
        }
    }

    private static DateTimeOffset? Date(string text, string field, List<string> problems)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date;
        }

        problems.Add($"{field} '{text}' is not a date.");
        return null;
    }

    private static bool Yes(string text) => text.Length > 0 && !No.Contains(text, StringComparer.OrdinalIgnoreCase);
}
