namespace EvidenceHound.Collectors.Sophos;

/// <summary>The lists the collector reads.</summary>
internal static class SophosSources
{
    public static readonly SophosSource Endpoints = new("endpoints", "endpoint/v1/endpoints", SophosPaging.Keyed);

    public static readonly SophosSource Firewalls = new("firewalls", "firewall/v1/firewalls", SophosPaging.Numbered);

    public static IReadOnlyList<SophosSource> All { get; } = [Endpoints, Firewalls];
}
