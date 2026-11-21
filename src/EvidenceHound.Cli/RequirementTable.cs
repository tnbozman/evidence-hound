using System.Text;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Cli;

/// <summary>Renders requirement results as a plain-text table.</summary>
internal static class RequirementTable
{
    public static string Render(IReadOnlyList<RequirementResult> rows)
    {
        string[] header = ["REQUIREMENT", "RESULT", "EVIDENCE", "OBSERVATION"];
        var cells = rows
            .Select(r => new[] { r.RequirementRef, Names.Of(r.Result), r.EvidenceRefs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Observation })
            .Prepend(header)
            .ToArray();
        var widths = Enumerable.Range(0, header.Length - 1).Select(column => cells.Max(row => row[column].Length)).ToArray();

        var text = new StringBuilder();
        foreach (var row in cells)
        {
            text.AppendJoin("  ", row.Take(widths.Length).Select((cell, column) => cell.PadRight(widths[column])))
                .Append("  ")
                .AppendLine(row[^1]);
        }

        return text.ToString();
    }
}
