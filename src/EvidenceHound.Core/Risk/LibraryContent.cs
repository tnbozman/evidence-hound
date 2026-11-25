using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Core.Risk;

/// <summary>
/// The digest of a library's <c>objects</c> (s15-01), the same whether they come from the repo's YAML file or from the content CISO Assistant
/// stores: keys in order, numbers by value, so a different key order or <c>1.50</c> against <c>1.5</c> is no change. Pure.
/// </summary>
public static class LibraryContent
{
    public static string Digest(JsonNode? objects)
    {
        var canonical = new StringBuilder();
        Write(canonical, objects);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Write(StringBuilder text, JsonNode? node)
    {
        switch (node)
        {
            case null:
                text.Append('z');
                break;
            case JsonObject map:
                text.Append('{');
                foreach (var (key, value) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    text.Append(JsonSerializer.Serialize(key)).Append(':');
                    Write(text, value);
                    text.Append(',');
                }

                text.Append('}');
                break;
            case JsonArray list:
                text.Append('[');
                foreach (var item in list)
                {
                    Write(text, item);
                    text.Append(',');
                }

                text.Append(']');
                break;
            case JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                text.Append(value.GetValueKind() == JsonValueKind.True ? "b:true" : "b:false");
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                text.Append("n:").Append(double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Null:
                text.Append('z');
                break;
            default:
                text.Append("s:").Append(JsonSerializer.Serialize(node.GetValue<string>()));
                break;
        }
    }
}
