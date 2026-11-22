using System.Globalization;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The parts of the tenant's lists that the checks read.</summary>
internal sealed record SophosData(IReadOnlyList<SophosEndpoint> Endpoints, IReadOnlyList<SophosFirewall> Firewalls)
{
    public static SophosData Parse(JsonArray endpoints, JsonArray firewalls)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(firewalls);
        return new(
            [.. endpoints.OfType<JsonObject>().Select(e => new SophosEndpoint(
                Text(e["id"]) ?? string.Empty,
                Text(e["health"]?["overall"]) ?? "unknown",
                e["tamperProtectionEnabled"] is JsonValue tamper && tamper.TryGetValue<bool>(out var on) ? on : null,
                Time(e["lastSeenAt"])))],
            [.. firewalls.OfType<JsonObject>().Select(f => new SophosFirewall(Text(f["id"]) ?? string.Empty, Text(f["firmwareVersion"])))]);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateTimeOffset? Time(JsonNode? node) =>
        Text(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}

/// <summary>One endpoint: its overall health, whether tamper protection is on (null when Sophos did not say), and when it was last seen.</summary>
internal sealed record SophosEndpoint(string Id, string Health, bool? TamperProtection, DateTimeOffset? LastSeen);

/// <summary>One firewall and the firmware version it reports.</summary>
internal sealed record SophosFirewall(string Id, string? Firmware);
