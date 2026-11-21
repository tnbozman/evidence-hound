using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Keeps personal data out of evidence: user principal names, mail addresses, people's display names and device names are
/// replaced by a short SHA-256 hash unless identities are included. Object IDs are kept, so findings can still be traced.
/// </summary>
internal static class Privacy
{
    /// <summary>Properties that always identify a person.</summary>
    private static readonly HashSet<string> Identities = new(StringComparer.Ordinal)
    {
        "userPrincipalName", "mail", "otherMails", "userDisplayName", "emailAddress", "deviceName", "givenName", "surname",
    };

    /// <summary>Properties that name a person only on an object that is a person, that is one with a user principal name.</summary>
    private static readonly HashSet<string> PersonNames = new(StringComparer.Ordinal) { "displayName" };

    public static JsonArray Minimise(JsonArray items, bool includeIdentities)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.DeepClone().AsArray();
        if (!includeIdentities)
        {
            foreach (var item in copy)
            {
                Walk(item);
            }
        }

        return copy;
    }

    /// <summary>A short, stable, case-insensitive hash: the same person gets the same value in every file and every run.</summary>
    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return "sha256:" + Convert.ToHexStringLower(digest)[..16];
    }

    private static void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var isPerson = obj.ContainsKey("userPrincipalName");
                foreach (var (name, value) in obj.ToArray())
                {
                    if (Identities.Contains(name) || (isPerson && PersonNames.Contains(name)))
                    {
                        obj[name] = Hashed(value);
                    }
                    else
                    {
                        Walk(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item);
                }

                break;
        }
    }

    private static JsonNode? Hashed(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) => JsonValue.Create(Hash(text)),
        JsonArray array => new JsonArray([.. array.Select(Hashed)]),
        _ => value?.DeepClone(),
    };
}
