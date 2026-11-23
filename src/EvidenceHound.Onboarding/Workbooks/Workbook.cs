using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Workbooks;

/// <summary>One sheet of a workbook as text: each row with its number in the sheet, counted from 1, and its cells left to right.</summary>
public sealed record Sheet(string Name, IReadOnlyList<(int Number, IReadOnlyList<string> Cells)> Rows);

/// <summary>
/// Reads an audit workbook's sheet without a spreadsheet program: a <c>.csv</c> file, or an <c>.xlsx</c> file (a zip of XML). Only cell
/// text is read; formulas, formats and macros are not. Pure, given the bytes.
/// </summary>
public static class Workbook
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static Result<Sheet> Read(string fileName, byte[] content, string? sheet)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => Result.Ok(Csv(Path.GetFileNameWithoutExtension(fileName), new UTF8Encoding(false).GetString(content).TrimStart('﻿'))),
            ".xlsx" => Xlsx(fileName, content, sheet),
            var other => Result.Fail<Sheet>(new Problem("import.format_unknown", $"{fileName}: hound reads .xlsx and .csv workbooks, not '{other}'. Save the sheet as one of those.")),
        };
    }

    /// <summary>Comma-separated text, with quoted cells that may hold commas, line breaks and doubled quotes.</summary>
    public static Sheet Csv(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<(int, IReadOnlyList<string>)>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        void EndRow()
        {
            cells.Add(cell.ToString());
            cell.Clear();
            rows.Add((rows.Count + 1, cells.ToArray()));
            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n')
            {
                EndRow();
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            EndRow();
        }

        return new Sheet(name, rows);
    }

    private static Result<Sheet> Xlsx(string fileName, byte[] content, string? wanted)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            var sheets = Load(zip, "xl/workbook.xml")?.Descendants(Main + "sheet").Select(s => (Name: (string?)s.Attribute("name") ?? "", Id: (string?)s.Attribute(Relationships + "id"))).ToArray() ?? [];
            var index = wanted is null ? 0 : Array.FindIndex(sheets, s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (sheets.Length == 0 || index < 0)
            {
                return Result.Fail<Sheet>(new Problem(
                    "import.sheet_unknown", $"{fileName} has no sheet '{wanted}'. Its sheets: {string.Join(", ", sheets.Select(s => s.Name))}."));
            }

            var target = Load(zip, "xl/_rels/workbook.xml.rels")?.Descendants(PackageRelationships + "Relationship")
                .FirstOrDefault(r => (string?)r.Attribute("Id") == sheets[index].Id)?.Attribute("Target")?.Value;
            var part = target is null ? $"xl/worksheets/sheet{index + 1}.xml" : target.StartsWith('/') ? target[1..] : $"xl/{target}";
            if (Load(zip, part) is not { } xml)
            {
                return Result.Fail<Sheet>(new Problem("import.workbook_unreadable", $"{fileName}: the sheet '{sheets[index].Name}' is missing from the file."));
            }

            var shared = Load(zip, "xl/sharedStrings.xml")?.Descendants(Main + "si").Select(Text).ToArray() ?? [];
            var rows = new List<(int, IReadOnlyList<string>)>();
            foreach (var row in xml.Descendants(Main + "row"))
            {
                var cells = new List<string>();
                foreach (var c in row.Elements(Main + "c"))
                {
                    var column = Column((string?)c.Attribute("r")) ?? cells.Count;
                    while (cells.Count < column)
                    {
                        cells.Add("");
                    }

                    cells.Add(((string?)c.Attribute("t")) switch
                    {
                        "s" => int.TryParse(c.Element(Main + "v")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var at) && at < shared.Length ? shared[at] : "",
                        "inlineStr" => c.Element(Main + "is") is { } inline ? Text(inline) : "",
                        _ => c.Element(Main + "v")?.Value ?? "",
                    });
                }

                rows.Add((int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : rows.Count + 1, cells));
            }

            return Result.Ok(new Sheet(sheets[index].Name, rows));
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            return Result.Fail<Sheet>(new Problem("import.workbook_unreadable", $"{fileName} is not a readable .xlsx workbook. Save it again from your spreadsheet program, or export the sheet as .csv."));
        }
    }

    /// <summary>An Excel day number as a date, for a date cell read as its stored number; other text is returned as it is.</summary>
    public static string Date(string cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var days) && days is > 20000 and < 80000
            ? new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified).AddDays(Math.Floor(days)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : cell;
    }

    private static string Text(XElement item) => string.Concat(item.Descendants(Main + "t").Select(t => t.Value));

    /// <summary>The zero-based column of a cell reference such as <c>C7</c>.</summary>
    private static int? Column(string? reference)
    {
        var letters = reference?.TakeWhile(char.IsAsciiLetter).ToArray() ?? [];
        return letters.Length == 0 ? null : letters.Aggregate(0, (sum, letter) => (sum * 26) + (char.ToUpperInvariant(letter) - 'A' + 1)) - 1;
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}
