using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// One Graph collection to read: its path relative to the Graph base address, and the application permission it needs. A request with
/// a <see cref="Body"/> is a POST, such as an advanced hunting query; <see cref="ItemsProperty"/> is where its rows are in the answer.
/// </summary>
internal sealed record GraphRequest(string Path, string Permission)
{
    public JsonObject? Body { get; init; }

    public string ItemsProperty { get; init; } = "value";
}

/// <summary>Every item of a Graph collection, read across all its pages.</summary>
internal sealed record GraphCollection(GraphRequest Request, JsonArray Items, int Pages);
