using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>Device checks over Intune-managed Windows devices: compliance and supported Windows builds.</summary>
internal static class DeviceChecks
{
    private static readonly string NoDevices = "No Intune-managed Windows devices were found.";

    public static Finding CompliantRatio(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.managed.compliant_ratio";
        string[] sources = [GraphSources.ManagedDevices];
        var windows = tenant.Devices.Where(d => d.IsWindows).ToArray();
        if (windows.Length == 0)
        {
            return new(id, CheckOutcome.Fail, NoDevices, sources);
        }

        var compliant = windows.Count(d => d.IsCompliant);
        var percent = 100.0 * compliant / windows.Length;
        var outcome = percent >= options.CompliantPassPercent ? CheckOutcome.Pass
            : percent >= options.CompliantPartialPercent ? CheckOutcome.Partial
            : CheckOutcome.Fail;
        var observation = string.Create(
            CultureInfo.InvariantCulture,
            $"{compliant} of {windows.Length} Intune-managed Windows devices ({percent:0.#} %) are compliant. Pass at {options.CompliantPassPercent:0.#} %, partial at {options.CompliantPartialPercent:0.#} %.");
        return new(id, outcome, observation, sources);
    }

    public static Finding SupportedVersions(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.os.supported_versions";
        string[] sources = [GraphSources.ManagedDevices];
        var windows = tenant.Devices.Where(d => d.IsWindows).ToArray();
        if (windows.Length == 0)
        {
            return new(id, CheckOutcome.Fail, NoDevices, sources);
        }

        var supported = string.Join(", ", options.SupportedWindowsBuilds);
        var unsupported = windows.Where(d => !options.SupportedWindowsBuilds.Contains(d.Build, StringComparer.Ordinal)).ToArray();
        if (unsupported.Length == 0)
        {
            return new(id, CheckOutcome.Pass, $"All {windows.Length} Intune-managed Windows devices run a supported build ({supported}).", sources);
        }

        var builds = string.Join(", ", unsupported.GroupBy(d => d.Build, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{(g.Key.Length == 0 ? "unknown" : g.Key)} ({g.Count()})"));
        return new(id, CheckOutcome.Fail, $"{unsupported.Length} of {windows.Length} Intune-managed Windows devices run an unsupported build: {builds}. Supported: {supported}.", sources);
    }
}
