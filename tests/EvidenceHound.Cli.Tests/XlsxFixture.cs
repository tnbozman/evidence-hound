using System.IO.Compression;
using System.Security;
using System.Text;

namespace EvidenceHound.Cli.Tests;

/// <summary>Writes a small .xlsx workbook, one sheet, the way a spreadsheet program does: shared strings, and cells addressed by column letter.</summary>
internal static class XlsxFixture
{
    public static void Write(string path, string[][] rows)
    {
        var strings = rows.SelectMany(r => r).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                // An empty cell is left out, as spreadsheet programs do, so the reader must place cells by their reference.
                if (rows[r][c].Length > 0)
                {
                    sheet.Append(System.Globalization.CultureInfo.InvariantCulture, $"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"s\"><v>{strings.IndexOf(rows[r][c])}</v></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        var shared = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + string.Concat(strings.Select(s => $"<si><t>{SecurityElement.Escape(s)}</t></si>")) + "</sst>";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets><sheet name=\"Audit\" sheetId=\"1\"/></sheets></workbook>");
        Add(zip, "xl/sharedStrings.xml", shared);
        Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
