using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>One Graph collection to read: its path relative to the Graph base address, and the application permission it needs.</summary>
internal sealed record GraphRequest(string Path, string Permission);

/// <summary>Every item of a Graph collection, read across all its pages.</summary>
internal sealed record GraphCollection(GraphRequest Request, JsonArray Items, int Pages);
