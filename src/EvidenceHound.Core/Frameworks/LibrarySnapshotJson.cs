using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Frameworks;

/// <summary>Reads and writes a library snapshot file (<c>libraries/&lt;library&gt;.json</c>). Output is indented and stable.</summary>
public static class LibrarySnapshotJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(LibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var json = new JsonObject
        {
            ["library_urn"] = snapshot.LibraryUrn,
            ["version"] = snapshot.Version,
            ["dependencies"] = Names(snapshot.Dependencies),
            ["matrices"] = new JsonArray([.. snapshot.Matrices.Select(m => new JsonObject
            {
                ["urn"] = m.Urn,
                ["name"] = m.Name,
                ["probability"] = Names(m.Probability),
                ["impact"] = Names(m.Impact),
                ["risk"] = Names(m.Risk),
            })]),
            ["threats"] = new JsonArray([.. snapshot.Threats.Select(t => new JsonObject { ["urn"] = t.Urn, ["ref_id"] = t.RefId, ["name"] = t.Name })]),
        };
        return json.ToJsonString(Indented) + "\n";
    }

    public static Result<LibrarySnapshot> Read(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("the file is not a JSON object");
            return Result.Ok(new LibrarySnapshot(
                Text(root, "library_urn"),
                root["version"]?.GetValue<int>() ?? throw new JsonException("'version' is missing"),
                Strings(root, "dependencies"),
                [.. Items(root, "matrices").Select(m => new MatrixSnapshot(Text(m, "urn"), Text(m, "name"), Strings(m, "probability"), Strings(m, "impact"), Strings(m, "risk")))],
                [.. Items(root, "threats").Select(t => new Threat(Text(t, "urn"), (string?)t["ref_id"], Text(t, "name")))]));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Result.Fail<LibrarySnapshot>(new Problem("library.snapshot_invalid", $"{source}: not a valid library snapshot: {ex.Message}"));
        }
    }

    private static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(n => JsonValue.Create(n))]);

    private static string Text(JsonNode node, string key) => (string?)node[key] ?? throw new JsonException($"'{key}' is missing");

    private static string[] Strings(JsonNode node, string key) => [.. Items(node, key).Select(n => n.GetValue<string>())];

    private static IEnumerable<JsonNode> Items(JsonNode node, string key) => (node[key] as JsonArray ?? []).OfType<JsonNode>();
}
