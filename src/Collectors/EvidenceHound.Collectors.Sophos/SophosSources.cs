namespace EvidenceHound.Collectors.Sophos;

/// <summary>The lists the collector reads.</summary>
internal static class SophosSources
{
    public static readonly SophosSource Firewalls = new("firewalls", "firewall/v1/firewalls");

    public static IReadOnlyList<SophosSource> All { get; } = [Firewalls];
}
