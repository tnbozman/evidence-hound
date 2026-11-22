using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// One evidence file per query: the request as written (secrets left as placeholders, headers never saved), page and call counts, and
/// every item. Properties the profile lists as identities are replaced by a short SHA-256 hash, at any depth, unless the instance
/// includes identities. A file that would hold the value of a secret in use is refused, not saved.
/// </summary>
public static class QueryEvidence
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string FileName(string query) => query + ".json";

    public static Result<CollectedEvidence> File(QueryResult.Collected query, bool includeIdentities, IReadOnlyCollection<Secret> secretsInUse)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(secretsInUse);
        var identities = query.Identities.ToHashSet(StringComparer.Ordinal);
        var content = new JsonObject
        {
            ["query"] = query.Name,
            ["request"] = query.Request,
            ["pages"] = query.Pages,
            ["calls"] = query.Calls,
            ["item_count"] = query.Items.Count,
            ["identities"] = identities.Count == 0 ? "none" : includeIdentities ? "included" : "hashed",
            ["items"] = includeIdentities || identities.Count == 0 ? query.Items.DeepClone() : new JsonArray([.. query.Items.Select(i => Minimise(i, identities))]),
        };
        return ContainsSecret(content, secretsInUse)
            ? Result.Fail<CollectedEvidence>(new Problem(
                "rest.secret_in_evidence", $"The evidence for query '{query.Name}' would contain the value of a secret in use, so it was not saved and the query cannot be assessed."))
            : Result.Ok(CollectedEvidence.FromBytes(FileName(query.Name), "application/json", Encoding.UTF8.GetBytes(content.ToJsonString(Indented))));
    }

    /// <summary>A short, stable, case-insensitive hash: the same person or device gets the same value in every file and every run.</summary>
    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant())))[..16];
    }

    /// <summary>True when any text in the tree (names or values) holds a secret's value.</summary>
    public static bool ContainsSecret(JsonNode? node, IReadOnlyCollection<Secret> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var values = secrets.Select(s => s.Reveal()).Where(v => v.Length >= 4).ToArray();
        return values.Length > 0 && Texts(node).Any(text => values.Any(v => text.Contains(v, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Texts(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(p => Texts(p.Value).Prepend(p.Key)),
        JsonArray array => array.SelectMany(Texts),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => [],
    };

    private static JsonNode? Minimise(JsonNode? node, HashSet<string> identities) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, identities.Contains(p.Key) ? Hashed(p.Value) : Minimise(p.Value, identities)))),
        JsonArray array => new JsonArray([.. array.Select(i => Minimise(i, identities))]),
        _ => node?.DeepClone(),
    };

    private static JsonNode? Hashed(JsonNode? value) => value switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var text) => JsonValue.Create(Hash(text)),
        JsonArray array => new JsonArray([.. array.Select(Hashed)]),
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Hashed(p.Value)))),
        _ => JsonValue.Create(Hash(value.ToJsonString())),
    };
}
