using System.Text;

namespace EvidenceHound.Collectors;

/// <summary>One CSV row: the line it starts on (from 1) and its cells.</summary>
public sealed record CsvRow(int Line, IReadOnlyList<string> Cells);

/// <summary>Splits CSV (RFC 4180: quoted cells, doubled quotes, commas and line breaks inside quotes) into rows, keeping each row's line.</summary>
public static class CsvRows
{
    public static IEnumerable<CsvRow> Read(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        return ReadRows(csv);
    }

    private static IEnumerable<CsvRow> ReadRows(string csv)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var rowLine = 1;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
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
                    line += c == '\n' ? 1 : 0;
                    cell.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    cells.Add(cell.ToString());
                    yield return new CsvRow(rowLine, cells);
                    cells = [];
                    cell.Clear();
                    line++;
                    rowLine = line;
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            yield return new CsvRow(rowLine, cells);
        }
    }
}
