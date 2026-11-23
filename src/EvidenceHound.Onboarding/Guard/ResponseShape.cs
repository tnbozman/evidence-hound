using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EvidenceHound.Onboarding.Guard;

/// <summary>
/// A response reduced to what a model needs to write a profile: field names, types and counts. Values are kept only for settings-like fields
/// (a few distinct short values, none matching an identity pattern) and never for the profile's <c>identities</c>. Pure.
/// </summary>
public static partial class ResponseShape
{
    internal const int MaxDistinct = 5;
    internal const int MaxLength = 40;
    internal const int MaxWords = 4;

    public static JsonObject Of(JsonNode? response, IEnumerable<string> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        return Shape([response], null, identities.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static JsonObject Shape(IReadOnlyList<JsonNode?> samples, string? name, HashSet<string> identities)
    {
        var present = samples.Where(s => s is not null).Select(s => s!).ToArray();
        return present.FirstOrDefault() switch
        {
            null => new JsonObject { ["type"] = "null" },
            JsonObject => ObjectShape(present.OfType<JsonObject>().ToArray(), identities),
            JsonArray => ArrayShape(present.OfType<JsonArray>().ToArray(), name, identities),
            JsonValue first => ValueShape(present.OfType<JsonValue>().ToArray(), first.GetValueKind(), name, identities),
            _ => new JsonObject { ["type"] = "unknown" },
        };
    }

    private static JsonObject ObjectShape(JsonObject[] objects, HashSet<string> identities)
    {
        var keys = objects.SelectMany(o => o.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        return new JsonObject(keys.Select(k => KeyValuePair.Create(k, (JsonNode?)Shape([.. objects.Select(o => o[k])], k, identities))));
    }

    private static JsonObject ArrayShape(JsonArray[] arrays, string? name, HashSet<string> identities) =>
        new()
        {
            ["type"] = "array",
            ["count"] = arrays.Sum(a => a.Count),
            ["items"] = Shape([.. arrays.SelectMany(a => a)], name, identities),
        };

    private static JsonObject ValueShape(JsonValue[] values, JsonValueKind kind, string? name, HashSet<string> identities)
    {
        var type = kind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "unknown",
        };
        var shape = new JsonObject { ["type"] = type };
        if (type is "string" or "boolean" && (name is null || !identities.Contains(name)))
        {
            var texts = values.Select(v => v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (texts.Length <= MaxDistinct && texts.All(SettingLike))
            {
                shape["values"] = new JsonArray([.. texts.Select(t => JsonValue.Create(t))]);
            }
        }

        return shape;
    }

    private static bool SettingLike(string text) =>
        text.Length <= MaxLength
        && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxWords
        && !OutboundGuard.Identities(text).Any()
        && !Identifier().IsMatch(text);

    /// <summary>
    /// Email addresses (fictional ones too), GUIDs, long runs of digits, long hex strings and dates: people, serial numbers, IDs, tokens and
    /// when things happened, which are data, not settings.
    /// </summary>
    [GeneratedRegex(@"[^\s@]+@[^\s@]+|[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-|\d{6,}|[0-9A-Fa-f]{16,}|\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
