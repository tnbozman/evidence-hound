using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>
/// One evidence file per list: the request, how many pages were read, and every item. Unless identities are included, host
/// names, people, addresses and serial numbers are replaced by a short SHA-256 hash, and locations are left out. IDs are kept, so
/// findings can still be traced in Sophos Central.
/// </summary>
internal static class SophosEvidence
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Properties, at any depth, that identify a device, a person, an organisation or a network.</summary>
    private static readonly HashSet<string> Identifying = new(StringComparer.Ordinal)
    {
        "hostname", "name", "serialNumber", "ipv4Addresses", "ipv6Addresses", "macAddresses", "externalIpv4Addresses", "externalIpv6Addresses", "internalIpv4Addresses",
        "firstName", "lastName", "email", "viaLogin",
    };

    private static readonly HashSet<string> Locations = new(StringComparer.Ordinal) { "geoLocation" };

    public static CollectedEvidence File(SophosCollection collection, bool includeIdentities)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var keep = includeIdentities || !collection.Source.HasIdentities;
        var content = new JsonObject
        {
            ["request"] = collection.Source.Path,
            ["pages"] = collection.Pages,
            ["identities"] = !collection.Source.HasIdentities ? "none" : includeIdentities ? "included" : "hashed",
            ["value"] = keep ? collection.Items.DeepClone() : new JsonArray([.. collection.Items.Select(Minimise)]),
        };
        return CollectedEvidence.FromBytes(collection.Source.EvidenceFile, "application/json", Encoding.UTF8.GetBytes(content.ToJsonString(Indented)));
    }

    /// <summary>A short, stable, case-insensitive hash: the same device gets the same value in every file and every run.</summary>
    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant())))[..16];
    }

    /// <summary>A copy with locations removed and identifying properties hashed, in nested objects and arrays too.</summary>
    private static JsonNode? Minimise(JsonNode? item) => item switch
    {
        JsonObject obj => new JsonObject(obj
            .Where(p => !Locations.Contains(p.Key))
            .Select(p => KeyValuePair.Create(p.Key, Identifying.Contains(p.Key) ? Hashed(p.Value) : Minimise(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Minimise)]),
        _ => item?.DeepClone(),
    };

    private static JsonNode? Hashed(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) => JsonValue.Create(Hash(text)),
        JsonArray array => new JsonArray([.. array.Select(Hashed)]),
        _ => value?.DeepClone(),
    };
}
