using System.Text.RegularExpressions;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>What one check found, citing the lists (by name) it read.</summary>
internal sealed record Finding(string CheckId, CheckOutcome Outcome, string Observation, IReadOnlyList<string> Sources);

/// <summary>One check the collector reports: its ID and the pure function that evaluates it.</summary>
internal sealed record SophosCheck(string Id, Func<SophosData, SophosOptions, DateTimeOffset, Finding> Evaluate);

/// <summary>Checks over the network devices a Sophos Central tenant manages.</summary>
internal static partial class SophosChecks
{
    private static readonly string[] FirewallSources = [SophosSources.Firewalls.Name];

    private static readonly string[] AdminSources = [SophosSources.Admins.Name, SophosSources.Roles.Name];

    public static IReadOnlyList<SophosCheck> All { get; } =
    [
        new("network.firewall.firmware_supported", FirmwareSupported),
        new("network.management.super_admin_count", SuperAdminCount),
    ];

    /// <summary>Few Sophos Central admins hold Super Admin, the role that can change every network device and every admin.</summary>
    public static Finding SuperAdminCount(SophosData data, SophosOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "network.management.super_admin_count";
        var superAdmin = data.Roles.Where(r => r.Type == "predefined" && IsSuperAdminName(r.Name)).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (superAdmin.Count == 0)
        {
            return new(id, CheckOutcome.Error, "Sophos Central listed no predefined Super Admin role, so Super Admins could not be counted.", AdminSources);
        }

        var count = data.Admins.Count(a => a.RoleIds.Any(superAdmin.Contains));
        var holds = count == 1 ? "holds" : "hold";
        var observation = $"{count} of {data.Admins.Count} Sophos Central admins {holds} Super Admin; at most {options.SuperAdminMax} allowed.";
        return new(id, count <= options.SuperAdminMax ? CheckOutcome.Pass : CheckOutcome.Fail, observation, AdminSources);
    }

    private static bool IsSuperAdminName(string name) =>
        string.Equals(name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal), "superadmin", StringComparison.OrdinalIgnoreCase);

    public static Finding FirmwareSupported(SophosData data, SophosOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "network.firewall.firmware_supported";
        var firewalls = data.Firewalls;
        var supported = string.Join(", ", options.SupportedFirmware);
        if (firewalls.Count == 0)
        {
            return new(id, CheckOutcome.NotApplicable, "No firewalls are managed in Sophos Central.", FirewallSources);
        }

        var versions = firewalls.Select(f => FirmwareVersion(f.Firmware)).ToArray();
        var unsupported = versions.Where(v => !IsSupported(v, options.SupportedFirmware)).ToArray();
        if (unsupported.Length == 0)
        {
            var running = string.Join(", ", versions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return new(id, CheckOutcome.Pass, $"All {firewalls.Count} firewalls run supported firmware ({running}). Supported: {supported}.", FirewallSources);
        }

        var listed = string.Join(", ", unsupported.GroupBy(v => v.Length == 0 ? "unknown" : v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})"));
        return new(id, CheckOutcome.Fail, $"{unsupported.Length} of {firewalls.Count} firewalls run firmware that is not supported: {listed}. Supported: {supported}.", FirewallSources);
    }

    /// <summary>The dotted version in a firmware string such as <c>SFOS 21.0.1 MR-1-Build237</c>, or empty when there is none.</summary>
    internal static string FirmwareVersion(string? firmware) =>
        firmware is not null && DottedVersion().Match(firmware) is { Success: true } match ? match.Value : string.Empty;

    /// <summary>A version is supported when it is, or starts with, a supported version: 21.0.1 is in 21.0.</summary>
    private static bool IsSupported(string version, IReadOnlyList<string> supported) =>
        version.Length > 0 && supported.Any(s => version == s || version.StartsWith(s + ".", StringComparison.Ordinal));

    [GeneratedRegex(@"[0-9]+(\.[0-9]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex DottedVersion();
}
