namespace EvidenceHound.Collectors.Sophos;

/// <summary>The lists the collector reads.</summary>
internal static class SophosSources
{
    public static readonly SophosSource Firewalls = new("firewalls", "firewall/v1/firewalls");

    public static readonly SophosSource Admins = new("admins", "common/v1/admins");

    /// <summary>Roles name no one, so their names are kept in evidence.</summary>
    public static readonly SophosSource Roles = new("roles", "common/v1/roles", HasIdentities: false);

    public static IReadOnlyList<SophosSource> All { get; } = [Firewalls, Admins, Roles];
}
