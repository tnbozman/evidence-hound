using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Reads CSV (RFC 4180: quoted cells, doubled quotes, commas and line breaks inside quotes) with a header row into a list of objects.</summary>
public static class CsvTable
{
    public static Result<JsonArray> Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var rows = Rows(csv).Where(r => r.Cells is not [""]).ToArray();
        if (rows.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        var header = rows[0].Cells;
        var bad = rows.Skip(1).FirstOrDefault(r => r.Cells.Count != header.Count);
        if (bad.Cells is not null)
        {
            return Result.Fail<JsonArray>(new Problem(
                "rest.response_invalid", string.Create(CultureInfo.InvariantCulture, $"CSV line {bad.Line} has {bad.Cells.Count} cells; the header has {header.Count}.")));
        }

        return Result.Ok(new JsonArray([.. rows.Skip(1).Select(r => (JsonNode)new JsonObject(header.Zip(r.Cells, (h, c) => KeyValuePair.Create(h, (JsonNode?)JsonValue.Create(c)))))]));
    }

    private static IEnumerable<(int Line, List<string> Cells)> Rows(string csv)
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
                    yield return (rowLine, cells);
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
            yield return (rowLine, cells);
        }
    }
}
