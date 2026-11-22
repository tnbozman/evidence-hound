using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The parts of the tenant's lists that the checks read.</summary>
internal sealed record SophosData(IReadOnlyList<SophosEndpoint> Endpoints, IReadOnlyList<SophosFirewall> Firewalls)
{
    public static SophosData Parse(JsonArray endpoints, JsonArray firewalls) => throw new NotImplementedException();
}

/// <summary>One endpoint: its overall health, whether tamper protection is on (null when Sophos did not say), and when it was last seen.</summary>
internal sealed record SophosEndpoint(string Id, string Health, bool? TamperProtection, DateTimeOffset? LastSeen);

/// <summary>One firewall and the firmware version it reports.</summary>
internal sealed record SophosFirewall(string Id, string? Firmware);
