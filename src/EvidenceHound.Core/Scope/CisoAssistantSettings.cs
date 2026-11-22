using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The feature flags (with their defaults) and general settings of the CISO Assistant release the stack pins, from the snapshot committed
/// as <c>src/EvidenceHound.Core/Scope/ciso-assistant-settings.json</c>. Validation checks <c>settings.yaml</c> against it, offline.
/// </summary>
public sealed record CisoAssistantSettings(
    string Version, IReadOnlyDictionary<string, bool> FeatureFlags, IReadOnlySet<string> General, IReadOnlySet<string> SecretGeneral)
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static CisoAssistantSettings Current { get; } = Load();

    /// <summary>A value as compact JSON with object keys sorted, so the same value always has the same text, whichever side wrote it.</summary>
    public static string Json(JsonNode? value) => Sorted(value)?.ToJsonString(Compact) ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(Sorted)]),
        null => null,
        _ => node.DeepClone(),
    };

    private static CisoAssistantSettings Load()
    {
        using var stream = typeof(CisoAssistantSettings).Assembly.GetManifestResourceStream("EvidenceHound.Core.ciso-assistant-settings.json")
            ?? throw new InvalidOperationException("The CISO Assistant settings snapshot is not embedded.");
        var snapshot = JsonNode.Parse(stream)!;
        return new(
            snapshot["ciso_assistant"]!.GetValue<string>(),
            snapshot["feature_flags"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<bool>(), StringComparer.Ordinal),
            Names(snapshot["general_settings"]),
            Names(snapshot["secret_general_settings"]));
    }

    private static HashSet<string> Names(JsonNode? list) => [.. (list as JsonArray ?? []).Select(n => n!.GetValue<string>())];
}
