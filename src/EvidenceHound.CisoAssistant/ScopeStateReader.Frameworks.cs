using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Risk;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Loaded frameworks, their snapshots and stored library content.</summary>
internal sealed partial class ScopeStateReader
{
    /// <summary>A framework's snapshot from its own record and its assessable requirement nodes; null when either cannot be read.</summary>
    private static async Task<FrameworkSnapshot?> SnapshotAsync(Reading reading, JsonNode framework, Dictionary<string, (string? Urn, string? Version)> libraries)
    {
        var id = Id(framework);
        var held = (await reading.ObjectsAsync("frameworks", [id])).FirstOrDefault();
        var nodes = await reading.ListAsync($"requirement-nodes/?framework={Uri.EscapeDataString(id)}");
        if (held is null)
        {
            return null;
        }

        var libraryId = RelationId(held["library"]) ?? RelationId(framework["library"]);
        (string? Urn, string? Version)? library = libraryId is not null && libraries.TryGetValue(libraryId, out var found) ? found : null;
        var version = int.TryParse(library?.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
        SnapshotNode[] assessable =
        [
            .. nodes.Where(n => n["assessable"] is JsonValue a && a.TryGetValue<bool>(out var isAssessable) && isAssessable).Select(n => new SnapshotNode(
                Text(n["urn"]) ?? Id(n),
                Text(n["ref_id"]),
                Text(n["parent_urn"]),
                Strings(n["implementation_groups"]),
                Text(n["name"]) ?? Text(n["description"]) ?? Text(n["ref_id"]) ?? Text(n["urn"]) ?? "")),
        ];
        ImplementationGroup[] defined =
            [.. (held["implementation_groups_definition"] as JsonArray ?? []).OfType<JsonObject>().Select(g => new ImplementationGroup(Text(g["ref_id"]) ?? "", Text(g["name"]) ?? Text(g["ref_id"]) ?? ""))];
        return new FrameworkSnapshot(
            library?.Urn ?? (framework["library"] is JsonObject listed ? Text(listed["urn"]) : null) ?? "",
            Text(held["urn"]) ?? Text(framework["urn"]) ?? id,
            version,
            defined.Length > 0 ? defined : [.. assessable.SelectMany(n => n.Groups).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(g => new ImplementationGroup(g, g))],
            assessable);
    }

    /// <summary>
    /// The frameworks and other libraries the scope loads that CISO Assistant holds, each with its loaded library's version: a framework by its
    /// URN, another library (such as a mapping set) by its own URN.
    /// </summary>
    private static IEnumerable<ActualObject> Frameworks(ScopeStateQuery query, List<JsonNode> frameworks, List<JsonNode> libraries)
    {
        var versions = libraries.ToDictionary(Id, l => Text(l["version"]), StringComparer.Ordinal);
        foreach (var urn in query.FrameworkUrns.Distinct(StringComparer.Ordinal))
        {
            (bool Found, string? Version) version = frameworks.FirstOrDefault(f => Text(f["urn"]) == urn) is { } framework
                ? (true, RelationId(framework["library"]) is { } library ? versions.GetValueOrDefault(library) : null)
                : libraries.FirstOrDefault(l => Text(l["urn"]) == urn) is { } loaded ? (true, Text(loaded["version"])) : (false, null);
            if (version.Found)
            {
                yield return new ActualObject(new ScopeObject(ScopeKind.Framework, urn, Fields(("version", version.Version))), null);
            }
        }
    }

    /// <summary>
    /// The digest of each custom library's stored objects (<see cref="LibraryContent"/>), by the key of the object that loads it (s15-01).
    /// CISO Assistant v4.3.4 shows no checksum, so its stored <c>content</c> is digested as the repo's file is. A library it does not store has none.
    /// </summary>
    private static async Task<Dictionary<string, string>> StoredContentAsync(Reading reading, IReadOnlyDictionary<string, string> custom)
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, urn) in custom)
        {
            var stored = (await reading.ListAsync($"stored-libraries/?urn={Uri.EscapeDataString(urn)}")).FirstOrDefault(l => Text(l["urn"]) == urn);
            if (stored is not null && await reading.ObjectAsync($"stored-libraries/{Uri.EscapeDataString(Id(stored))}/content/", $"to read library {urn}") is { } content)
            {
                digests[key] = LibraryContent.Digest(content);
            }
        }

        return digests;
    }
}
