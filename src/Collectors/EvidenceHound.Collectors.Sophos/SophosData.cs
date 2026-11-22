using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The parts of the tenant's lists that the checks read.</summary>
internal sealed record SophosData(IReadOnlyList<SophosFirewall> Firewalls, IReadOnlyList<SophosAdmin> Admins, IReadOnlyList<SophosRole> Roles)
{
    public static SophosData Parse(JsonArray firewalls, JsonArray admins, JsonArray roles)
    {
        ArgumentNullException.ThrowIfNull(firewalls);
        ArgumentNullException.ThrowIfNull(admins);
        ArgumentNullException.ThrowIfNull(roles);
        return new(
            [.. firewalls.OfType<JsonObject>().Select(f => new SophosFirewall(Text(f["id"]) ?? string.Empty, Text(f["firmwareVersion"])))],
            [.. admins.OfType<JsonObject>().Select(a => new SophosAdmin(
                Text(a["id"]) ?? string.Empty,
                [.. (a["roleAssignments"] as JsonArray ?? []).OfType<JsonObject>().Select(r => Text(r["roleId"])).OfType<string>()]))],
            [.. roles.OfType<JsonObject>().Select(r => new SophosRole(Text(r["id"]) ?? string.Empty, Text(r["name"]) ?? string.Empty, Text(r["type"]) ?? string.Empty))]);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>One firewall and the firmware version it reports.</summary>
internal sealed record SophosFirewall(string Id, string? Firmware);

/// <summary>One Sophos Central admin and the IDs of the roles assigned to them.</summary>
internal sealed record SophosAdmin(string Id, IReadOnlyList<string> RoleIds);

/// <summary>One Sophos Central role: its ID, name and type (<c>predefined</c> or <c>custom</c>).</summary>
internal sealed record SophosRole(string Id, string Name, string Type);
