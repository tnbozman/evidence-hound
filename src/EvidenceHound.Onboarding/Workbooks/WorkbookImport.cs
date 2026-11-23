using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Workbooks;

/// <summary>One workbook row read as an audit result: where it came from, the requirement as the workbook names it, and what it records.</summary>
public sealed record ImportedRow(int Row, string Requirement, string Result, string Observation, string Evidence, string ApprovedBy, string ApprovedOn)
{
    /// <summary>A row the workbook says someone approved. Any other row is a draft until a person approves it.</summary>
    public bool Approved => ApprovedBy.Length > 0;
}

/// <summary>
/// Maps a workbook sheet to audit results: finds the columns by their headings, normalises each result, and keeps every row's number. Rows
/// with no requirement are left out, and rows not assessed are counted, not imported. Pure.
/// </summary>
public static class WorkbookImport
{
    /// <summary>What the import reads, with the headings it recognises when no <c>--column</c> names one.</summary>
    public static IReadOnlyList<(string Name, bool Required, string[] Headings)> Columns { get; } =
    [
        ("requirement", true, ["requirement", "requirement id", "ref", "ref_id", "control"]),
        ("result", true, ["result"]),
        ("observation", false, ["observation", "notes", "comment", "comments"]),
        ("evidence", false, ["evidence", "evidence reference"]),
        ("approved_by", false, ["approved by", "approver"]),
        ("approved_on", false, ["approved on", "approval date"]),
    ];

    private static readonly Dictionary<string, string> Results = new(StringComparer.Ordinal)
    {
        ["compliant"] = "compliant",
        ["non_compliant"] = "non_compliant",
        ["partially_compliant"] = "partially_compliant",
        ["partial"] = "partially_compliant",
        ["not_applicable"] = "not_applicable",
        ["n/a"] = "not_applicable",
        ["not_assessed"] = "not_assessed",
        [""] = "not_assessed",
    };

    /// <summary>The rows with a result to import, and how many were left out as not assessed.</summary>
    public static Result<(IReadOnlyList<ImportedRow> Rows, int NotAssessed)> Read(Sheet sheet, string workbook, IReadOnlyDictionary<string, string> named)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(named);
        var header = sheet.Rows.FirstOrDefault(r => r.Cells.Any(c => c.Trim().Length > 0));
        if (header.Cells is null)
        {
            return Result.Fail<(IReadOnlyList<ImportedRow>, int)>(new Problem("import.workbook_empty", $"{workbook} has no rows."));
        }

        var headings = header.Cells.Select(c => c.Trim()).ToArray();
        var problems = new List<Problem>();
        problems.AddRange(named.Keys.Where(k => !Columns.Any(c => c.Name == k))
            .Select(k => new Problem("import.column_unknown", $"'{k}' is not something the import reads; use {string.Join(", ", Columns.Select(c => c.Name))}.")));
        var at = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, required, defaults) in Columns)
        {
            var index = named.TryGetValue(name, out var heading)
                ? Array.FindIndex(headings, h => string.Equals(h, heading.Trim(), StringComparison.OrdinalIgnoreCase))
                : Array.FindIndex(headings, h => defaults.Contains(h.ToLowerInvariant(), StringComparer.Ordinal));
            if (index >= 0)
            {
                at[name] = index;
            }
            else if (required || heading is not null)
            {
                problems.Add(new Problem(
                    "import.column_missing",
                    $"{workbook} has no column for {name}{(heading is null ? "" : $" headed '{heading}'")}. Its headings: {string.Join(", ", headings.Where(h => h.Length > 0))}. Name it with --column {name}=<heading>."));
            }
        }

        if (problems.Count > 0)
        {
            return Result.Fail<(IReadOnlyList<ImportedRow>, int)>(problems);
        }

        var rows = new List<ImportedRow>();
        var notAssessed = 0;
        foreach (var (number, cells) in sheet.Rows.Where(r => r.Number > header.Number))
        {
            string Cell(string name) => at.TryGetValue(name, out var index) && index < cells.Count ? cells[index].Trim() : "";
            var requirement = Cell("requirement");
            if (requirement.Length == 0)
            {
                continue;
            }

            var written = Cell("result");
            if (!Results.TryGetValue(written.ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), out var result))
            {
                problems.Add(new Problem(
                    "import.result_unknown",
                    $"{workbook} row {number}: '{written}' is not a result hound knows. Use compliant, partially compliant, non-compliant, not applicable or not assessed."));
            }
            else if (result == "not_assessed")
            {
                notAssessed++;
            }
            else
            {
                rows.Add(new ImportedRow(number, requirement, result, Cell("observation"), Cell("evidence"), Cell("approved_by"), Workbook.Date(Cell("approved_on"))));
            }
        }

        return problems.Count > 0 ? Result.Fail<(IReadOnlyList<ImportedRow>, int)>(problems) : Result.Ok<(IReadOnlyList<ImportedRow>, int)>((rows, notAssessed));
    }

    /// <summary>
    /// The observation an imported row carries in the audit: what the workbook says, its evidence reference, then where it came from and who
    /// approved it, so the provenance travels with the row into the plan and CISO Assistant.
    /// </summary>
    public static string Observation(ImportedRow row, string workbook, string approvedBy, string approvedOn)
    {
        ArgumentNullException.ThrowIfNull(row);
        string[] parts =
        [
            Sentence(row.Observation),
            row.Evidence.Length > 0 ? Sentence($"Evidence: {row.Evidence}") : "",
            $"Imported from {workbook}, row {row.Row}, approved by {approvedBy} on {(approvedOn.Length > 0 ? approvedOn : "a date the workbook does not record")}.",
        ];
        return string.Join(' ', parts.Where(p => p.Length > 0));
    }

    private static string Sentence(string text) => text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : $"{text}.";
}
