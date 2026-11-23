using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Reads CSV (RFC 4180: quoted cells, doubled quotes, commas and line breaks inside quotes) with a header row into a list of objects.</summary>
public static class CsvTable
{
    public static Result<JsonArray> Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var rows = CsvRows.Read(csv).Where(r => r.Cells is not [""]).ToArray();
        if (rows.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        var header = rows[0].Cells;
        var bad = rows.Skip(1).FirstOrDefault(r => r.Cells.Count != header.Count);
        if (bad is not null)
        {
            return Result.Fail<JsonArray>(new Problem(
                "rest.response_invalid", string.Create(CultureInfo.InvariantCulture, $"CSV line {bad.Line} has {bad.Cells.Count} cells; the header has {header.Count}.")));
        }

        return Result.Ok(new JsonArray([.. rows.Skip(1).Select(r => (JsonNode)new JsonObject(header.Zip(r.Cells, (h, c) => KeyValuePair.Create(h, (JsonNode?)JsonValue.Create(c)))))]));
    }
}
