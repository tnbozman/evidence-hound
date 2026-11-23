using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.SophosDevServer;

/// <summary>Which tenant the dev server answers as: every control in place, or every named deviation applied.</summary>
public enum DevVariant
{
    Compliant,
    Gaps,
}

/// <summary>
/// A firewall Sophos Central manages. <paramref name="Firmware"/> is as Sophos Central reports it, such as <c>SFOS 21.0.1 MR-1-Build237</c>.
/// <paramref name="UpgradeTo"/> is the firmware upgrade Sophos Central offers it, if any, and <paramref name="OfferedDays"/> how many
/// daily runs have seen that offer before today (the API itself gives no date).
/// </summary>
public sealed record Firewall(string Id, string Hostname, string Name, string SerialNumber, string Model, string Firmware, string? UpgradeTo = null, int OfferedDays = 0);

/// <summary>A Sophos Central admin and the IDs of the roles assigned to them.</summary>
public sealed record Admin(string Id, string FirstName, string LastName, string Email, IReadOnlyList<string> RoleIds);

/// <summary>A predefined Sophos Central role.</summary>
public sealed record Role(string Id, string Name, string PrincipalType, string Description);

/// <summary>A named way the <c>gaps</c> tenant differs from the compliant one, and the outcome each check it breaks is expected to have.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<SophosDevTenant, SophosDevTenant> Apply);

/// <summary>
/// The fictional Example Pty Ltd Sophos Central tenant, described once. <see cref="SophosAnswers"/> renders it as the answers the
/// <c>sophos-central</c> profile reads, so the same IDs appear in every answer. Nothing here is real Sophos data.
/// </summary>
public sealed record SophosDevTenant(string TenantId, IReadOnlyList<Firewall> Firewalls, IReadOnlyList<Admin> Admins, IReadOnlyList<Role> Roles)
{
    /// <summary>The token the token address always answers. It is not a secret and opens nothing.</summary>
    public static readonly string AccessToken = "sophos-dev-token-not-a-secret";

    /// <summary>The client secret an instance reading the dev server is given. The server never checks it.</summary>
    public static readonly string ClientSecret = "sophos-dev-server-checks-no-secret";

    public static readonly string ClientId = Id(0x50d, 1);

    public static readonly string SupportedFirmware = "21.0, 21.5";

    private static readonly Role SuperAdmin = new(Id(0xa5, 1), "Super Admin", "user", "Full access");
    private static readonly Role AdminRole = new(Id(0xa5, 2), "Admin", "user", "Manage settings");
    private static readonly Role ReadOnly = new(Id(0xa5, 3), "Read-only", "user", "View only");
    private static readonly Role ServiceReadOnly = new(Id(0xa5, 4), "Service Principal Read-Only", "service", "API read only");

    /// <summary>Example Pty Ltd with every control the sophos-central profile checks in place.</summary>
    public static SophosDevTenant Compliant { get; } = new(
        Id(0x50a, 1),
        [
            new(Id(0xf0, 1), "fw-office.example.com", "Example office firewall", "X0000EXAMPLE01", "XGS88w", "SFOS 21.0.1 MR-1-Build237"),
            new(Id(0xf0, 2), "fw-branch.example.com", "Example branch firewall", "X0000EXAMPLE02", "XGS108w", "SFOS 21.5.0 GA-Build171"),
        ],
        [
            new(Id(0xad, 1), "Alex", "Admin", "alex.admin@example.com", [SuperAdmin.Id]),
            new(Id(0xad, 2), "Sam", "Admin", "sam.admin@example.com", [SuperAdmin.Id]),
            new(Id(0xad, 3), "Jo", "Example", "jo.example@example.com", [AdminRole.Id]),
            new(Id(0xad, 4), "Kim", "Example", "kim.example@example.com", [ReadOnly.Id]),
            new(Id(0xad, 5), "Lee", "Example", "lee.example@example.com", [ReadOnly.Id]),
        ],
        [SuperAdmin, AdminRole, ReadOnly, ServiceReadOnly]);

    /// <summary>The deviations the <c>gaps</c> tenant carries, each failing the checks it names and no other.</summary>
    public static IReadOnlyList<Deviation> Deviations { get; } =
    [
        new(
            "unsupported-firmware",
            "The branch firewall still runs SFOS 20.0, which is out of support.",
            Expect(("network.firewall.firmware_supported", CheckOutcome.Fail)),
            t => t with { Firewalls = [.. t.Firewalls.Select(f => f.Id == Id(0xf0, 2) ? f with { Firmware = "SFOS 20.0.3 MR-3-Build514" } : f)] }),
        new(
            "third-super-admin",
            "Kim was made a Super Admin: three Super Admins where at most two are allowed.",
            Expect(("network.management.super_admin_count", CheckOutcome.Fail)),
            t => t with { Admins = [.. t.Admins.Select(a => a.Id == Id(0xad, 4) ? a with { RoleIds = [SuperAdmin.Id] } : a)] }),
        new(
            "firmware-upgrade-overdue",
            "Sophos Central has offered the office firewall SFOS 21.5 for 20 days, more than the 14 allowed.",
            Expect(("network.firewall.firmware_current", CheckOutcome.Fail)),
            t => t with { Firewalls = [.. t.Firewalls.Select(f => f.Id == Id(0xf0, 1) ? f with { UpgradeTo = "21.5.0 GA-Build171", OfferedDays = 20 } : f)] }),
    ];

    public static SophosDevTenant For(DevVariant variant) =>
        variant == DevVariant.Compliant ? Compliant : Deviations.Aggregate(Compliant, (tenant, deviation) => tenant.With(deviation));

    /// <summary>Settings for a <c>rest</c> collector instance with the sophos-central profile that reads the dev server at <paramref name="url"/>.</summary>
    public static Dictionary<string, string> Settings(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new(StringComparer.Ordinal)
        {
            ["profile"] = "sophos-central",
            ["client_id"] = ClientId,
            ["id_url"] = url.ToString(),
            ["api_url"] = url.ToString(),
            ["supported_firewall_firmware"] = SupportedFirmware,
        };
    }

    public SophosDevTenant With(Deviation deviation)
    {
        ArgumentNullException.ThrowIfNull(deviation);
        return deviation.Apply(this);
    }

    /// <summary>A GUID-shaped ID that is obviously invented: group <paramref name="group"/>, number <paramref name="n"/>.</summary>
    internal static string Id(int group, int n) => string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{group:x8}{n:x4}");

    private static Dictionary<string, CheckOutcome> Expect(params (string Check, CheckOutcome Outcome)[] fails) =>
        fails.ToDictionary(f => f.Check, f => f.Outcome, StringComparer.Ordinal);
}
