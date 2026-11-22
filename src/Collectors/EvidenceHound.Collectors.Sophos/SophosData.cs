using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The parts of the tenant's lists that the checks read.</summary>
internal sealed record SophosData(IReadOnlyList<SophosFirewall> Firewalls)
{
    public static SophosData Parse(JsonArray firewalls)
    {
        ArgumentNullException.ThrowIfNull(firewalls);
        return new([.. firewalls.OfType<JsonObject>().Select(f => new SophosFirewall(Text(f["id"]) ?? string.Empty, Text(f["firmwareVersion"])))]);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>One firewall and the firmware version it reports.</summary>
internal sealed record SophosFirewall(string Id, string? Firmware);
