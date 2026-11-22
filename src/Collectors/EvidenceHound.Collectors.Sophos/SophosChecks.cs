using System.Globalization;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>What one check found, citing the lists (by name) it read.</summary>
internal sealed record Finding(string CheckId, CheckOutcome Outcome, string Observation, IReadOnlyList<string> Sources);

/// <summary>One check the collector reports: its ID and the pure function that evaluates it.</summary>
internal sealed record SophosCheck(string Id, Func<SophosData, SophosOptions, DateTimeOffset, Finding> Evaluate);

/// <summary>Endpoint protection and firewall checks over a Sophos Central tenant.</summary>
internal static partial class SophosChecks
{
    private static readonly string NoEndpoints = "No endpoints were found in Sophos Central.";

    private static readonly string[] EndpointSources = [SophosSources.Endpoints.Name];

    private static readonly string[] FirewallSources = [SophosSources.Firewalls.Name];

    public static IReadOnlyList<SophosCheck> All { get; } =
    [
        new("endpoint.protection.healthy_ratio", HealthyRatio),
        new("endpoint.protection.tamper_protection_enabled", TamperProtection),
        new("endpoint.protection.stale_agents", StaleAgents),
        new("network.firewall.firmware_supported", FirmwareSupported),
    ];

    public static Finding HealthyRatio(SophosData data, SophosOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.protection.healthy_ratio";
        var endpoints = data.Endpoints;
        if (endpoints.Count == 0)
        {
            return new(id, CheckOutcome.Fail, NoEndpoints, EndpointSources);
        }

        var good = endpoints.Count(e => e.Health == "good");
        var percent = 100.0 * good / endpoints.Count;
        var outcome = percent >= options.HealthyPassPercent ? CheckOutcome.Pass
            : percent >= options.HealthyPartialPercent ? CheckOutcome.Partial
            : CheckOutcome.Fail;
        var others = string.Join(", ", endpoints.Where(e => e.Health != "good").GroupBy(e => e.Health, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})"));
        var observation = string.Create(
            CultureInfo.InvariantCulture,
            $"{good} of {endpoints.Count} endpoints ({percent:0.#} %) are in good health{(others.Length == 0 ? string.Empty : $"; the others are {others}")}. Pass at {options.HealthyPassPercent:0.#} %, partial at {options.HealthyPartialPercent:0.#} %.");
        return new(id, outcome, observation, EndpointSources);
    }

    public static Finding TamperProtection(SophosData data, SophosOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        const string id = "endpoint.protection.tamper_protection_enabled";
        var endpoints = data.Endpoints;
        if (endpoints.Count == 0)
        {
            return new(id, CheckOutcome.Fail, NoEndpoints, EndpointSources);
        }

        var off = endpoints.Count(e => e.TamperProtection != true);
        return off == 0
            ? new(id, CheckOutcome.Pass, $"Tamper protection is on for all {endpoints.Count} endpoints.", EndpointSources)
            : new(id, CheckOutcome.Fail, $"Tamper protection is off or not reported for {off} of {endpoints.Count} endpoints.", EndpointSources);
    }

    public static Finding StaleAgents(SophosData data, SophosOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.protection.stale_agents";
        var endpoints = data.Endpoints;
        if (endpoints.Count == 0)
        {
            return new(id, CheckOutcome.Fail, NoEndpoints, EndpointSources);
        }

        var cutoff = now.AddDays(-options.StaleAgentDays);
        var stale = endpoints.Count(e => e.LastSeen is not { } seen || seen < cutoff);
        return stale == 0
            ? new(id, CheckOutcome.Pass, $"All {endpoints.Count} endpoints were seen by Sophos Central in the last {options.StaleAgentDays} days.", EndpointSources)
            : new(id, CheckOutcome.Fail, $"{stale} of {endpoints.Count} endpoints have not been seen by Sophos Central for more than {options.StaleAgentDays} days.", EndpointSources);
    }

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
