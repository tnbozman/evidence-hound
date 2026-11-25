using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Frameworks;

/// <summary>Reads and writes a framework snapshot file (<c>frameworks/&lt;library&gt;.json</c>). Output is indented and stable.</summary>
public static class FrameworkSnapshotJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(FrameworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var json = new JsonObject
        {
            ["library_urn"] = snapshot.LibraryUrn,
            ["framework_urn"] = snapshot.FrameworkUrn,
            ["version"] = snapshot.Version,
            ["groups"] = new JsonArray([.. snapshot.Groups.Select(g => new JsonObject { ["ref_id"] = g.RefId, ["name"] = g.Name })]),
            ["nodes"] = new JsonArray([.. snapshot.Nodes.Select(Node)]),
        };
        if (snapshot.Parents.Count > 0)
        {
            json["parents"] = new JsonArray([.. snapshot.Parents.Select(Parent)]);
        }

        return json.ToJsonString(Indented) + "\n";
    }

    /// <summary>A parent node; its own parent only when it has one, so snapshots of two-level frameworks stay unchanged.</summary>
    private static JsonObject Parent(SnapshotParent parent)
    {
        var json = new JsonObject { ["urn"] = parent.Urn, ["ref_id"] = parent.RefId, ["name"] = parent.Name };
        if (parent.ParentUrn is not null)
        {
            json["parent_urn"] = parent.ParentUrn;
        }

        return json;
    }

    /// <summary>A node; its description and reference controls only when it has them, so older snapshots stay unchanged.</summary>
    private static JsonObject Node(SnapshotNode node)
    {
        var json = new JsonObject
        {
            ["urn"] = node.Urn,
            ["ref_id"] = node.RefId,
            ["parent_urn"] = node.ParentUrn,
            ["groups"] = new JsonArray([.. node.Groups.Select(g => JsonValue.Create(g))]),
            ["title"] = node.Title,
        };
        if (node.Description.Length > 0)
        {
            json["description"] = node.Description;
        }

        if (node.ReferenceControls.Count > 0)
        {
            json["reference_controls"] = new JsonArray([.. node.ReferenceControls.Select(r => JsonValue.Create(r))]);
        }

        return json;
    }

    public static Result<FrameworkSnapshot> Read(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("the file is not a JSON object");
            return Result.Ok(new FrameworkSnapshot(
                Text(root, "library_urn"),
                Text(root, "framework_urn"),
                root["version"]?.GetValue<int>() ?? throw new JsonException("'version' is missing"),
                [.. Items(root, "groups").Select(g => new ImplementationGroup(Text(g, "ref_id"), Text(g, "name")))],
                [.. Items(root, "nodes").Select(n => new SnapshotNode(
                    Text(n, "urn"),
                    (string?)n["ref_id"],
                    (string?)n["parent_urn"],
                    [.. Items(n, "groups").Select(g => g.GetValue<string>())],
                    (string?)n["title"] ?? "")
                {
                    Description = (string?)n["description"] ?? "",
                    ReferenceControls = [.. Items(n, "reference_controls").Select(r => r.GetValue<string>())],
                })])
            {
                Parents = [.. Items(root, "parents").Select(p => new SnapshotParent(Text(p, "urn"), (string?)p["ref_id"], Text(p, "name")) { ParentUrn = (string?)p["parent_urn"] })],
            });
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Result.Fail<FrameworkSnapshot>(new Problem("framework.snapshot_invalid", $"{source}: not a valid framework snapshot: {ex.Message}"));
        }
    }

    private static string Text(JsonNode node, string key) =>
        (string?)node[key] ?? throw new JsonException($"'{key}' is missing");

    private static IEnumerable<JsonNode> Items(JsonNode node, string key) =>
        (node[key] as JsonArray ?? []).OfType<JsonNode>();
}
