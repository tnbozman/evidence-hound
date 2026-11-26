using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Application control checks over Intune: App Control for Business policies and AppLocker custom profiles, with their assignments.
/// Policies set by Group Policy are not in Graph, so a check that does not pass says so.
/// </summary>
internal static class AppControlChecks
{
    private static readonly string GroupPolicy = " Policies set by Group Policy are not seen.";

    private static readonly string[] Sources = [GraphSources.ConfigurationPolicies, GraphSources.DeviceConfigurations];

    private static readonly Dictionary<FileType, string> TypeNames = new()
    {
        [FileType.Executables] = "executables",
        [FileType.Libraries] = "software libraries",
        [FileType.Scripts] = "scripts",
        [FileType.Installers] = "installers",
    };

    public static Finding Enforced(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.application_control.enforced";
        var assigned = tenant.AppControl.Where(p => p.Assigned).ToArray();
        if (assigned.Length == 0)
        {
            return new(id, CheckOutcome.Fail, "No assigned App Control for Business policy or AppLocker profile was found." + GroupPolicy, Sources);
        }

        var enforcing = assigned.Where(p => p.Enforces).ToArray();
        if (enforcing.Length == 0)
        {
            var audit = assigned.Where(p => p.Modes.Values.Contains(ControlMode.Audit)).ToArray();
            var unknown = assigned.Where(p => p.Modes.Values.Contains(ControlMode.Unknown)).ToArray();
            var detail = (audit.Length > 0 ? $" In audit mode only: {Labels(audit)}." : string.Empty)
                + (unknown.Length > 0 ? $" Mode unknown, because Graph returned the rule collections encrypted: {Labels(unknown)}." : string.Empty);
            return new(id, CheckOutcome.Partial, "No application control policy is enforced." + detail + GroupPolicy, Sources);
        }

        if (enforcing.Where(p => p.AllDevices).ToArray() is { Length: > 0 } everywhere)
        {
            return new(id, CheckOutcome.Pass, $"Application control is enforced on all devices ({Labels(everywhere)}).", Sources);
        }

        var required = options.ApplicationControlGroups;
        if (required.Count == 0)
        {
            return new(id, CheckOutcome.Partial, $"Application control is enforced only for some groups, not all devices ({Labels(enforcing)}). List the groups that must be covered in {M365Settings.ApplicationControlGroups}." + GroupPolicy, Sources);
        }

        var covering = enforcing.Where(p => p.Groups.Intersect(required, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
        var missing = required.Except(covering.SelectMany(p => p.Groups), StringComparer.OrdinalIgnoreCase).ToArray();
        return missing.Length == 0
            ? new(id, CheckOutcome.Pass, $"Application control is enforced for every group in {M365Settings.ApplicationControlGroups} ({string.Join(", ", required)}) ({Labels(covering)}).", Sources)
            : new(id, CheckOutcome.Partial, $"Application control is enforced for {required.Count - missing.Length} of {required.Count} groups in {M365Settings.ApplicationControlGroups} ({Labels(covering.Length > 0 ? covering : enforcing)}), not for {string.Join(", ", missing)}." + GroupPolicy, Sources);
    }

    /// <summary>
    /// Allowed and blocked application control events reach Defender for Endpoint, the central log: events were received in the
    /// window, and enough Intune-managed Windows devices are onboarded to send them.
    /// </summary>
    public static Finding EventsCentrallyLogged(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "endpoint.application_control.events_centrally_logged";
        string[] sources = [GraphSources.AppControlEvents, GraphSources.BlockedFiles, GraphSources.DefenderOnboarding, GraphSources.ManagedDevices];
        if (sources.Select(s => tenant.Unavailable.GetValueOrDefault(s)).OfType<Core.Results.Problem>().ToArray() is { Length: > 0 } refused)
        {
            return refused.FirstOrDefault(p => p.Code == GraphClient.NotLicensed) is { } licence
                ? new(id, CheckOutcome.NotApplicable, $"Not applicable: the tenant has no licence that includes advanced hunting ({licence.Message}). Use the manual application control event log template instead.", [])
                : new(id, CheckOutcome.Error, $"Unable to assess: {refused[0].Message}", []);
        }

        var days = options.ApplicationControlWindowDays;
        var events = tenant.AppControlEvents.Sum(e => e.Events);
        if (events == 0)
        {
            return new(id, CheckOutcome.Fail, string.Create(CultureInfo.InvariantCulture, $"Defender for Endpoint received no application control events in the last {days} days, so allowed and blocked executions are not reaching the central log."), sources);
        }

        var windows = tenant.Devices.Count(d => d.IsWindows);
        var onboarded = tenant.DefenderOnboarding.GetValueOrDefault("Onboarded");
        var percent = windows == 0 ? 0 : Math.Min(100.0, 100.0 * onboarded / windows);
        var devices = tenant.AppControlEvents.Select(e => e.DeviceId).Distinct(StringComparer.Ordinal).Count();
        var received = string.Create(
            CultureInfo.InvariantCulture,
            $"{events} application control events from {devices} devices in the last {days} days ({tenant.AppControlEvents.Where(e => e.Blocked).Sum(e => e.Events)} blocked, {tenant.AppControlEvents.Where(e => e.Audited).Sum(e => e.Events)} audited)");
        var top = tenant.BlockedFiles.OrderByDescending(f => f.Blocks).FirstOrDefault() is { } file
            ? string.Create(CultureInfo.InvariantCulture, $" Most blocked: {file.FileName} ({file.Blocks}).")
            : string.Empty;
        var share = string.Create(CultureInfo.InvariantCulture, $"{onboarded} of {windows} Intune-managed Windows devices ({percent:0.#} %)");
        return percent >= options.DefenderOnboardedPercent
            ? new(id, CheckOutcome.Pass, $"Defender for Endpoint received {received}, and {share} are onboarded.{top}", sources)
            : new(id, CheckOutcome.Partial, string.Create(CultureInfo.InvariantCulture, $"Only {share} are onboarded to Defender for Endpoint, below {options.DefenderOnboardedPercent:0.#} %; it received {received}.{top}"), sources);
    }

    public static Finding FileTypesCovered(TenantData tenant, M365Options _)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        const string id = "endpoint.application_control.file_types_covered";
        var enforcing = tenant.AppControl.Where(p => p.Assigned && p.Enforces).ToArray();
        var covered = Enum.GetValues<FileType>().Where(t => enforcing.Any(p => p.Modes.GetValueOrDefault(t, ControlMode.Unknown) == ControlMode.Enforced)).ToArray();
        if (covered.Length == 0)
        {
            return new(id, CheckOutcome.Fail, "No enforced application control covers executables, software libraries, scripts or installers." + GroupPolicy, Sources);
        }

        var missing = Enum.GetValues<FileType>().Except(covered).ToArray();
        return missing.Length == 0
            ? new(id, CheckOutcome.Pass, $"Enforced application control covers {Prose.And(covered.Select(t => TypeNames[t]))} ({Labels(enforcing)}).", Sources)
            : new(id, CheckOutcome.Fail, $"Enforced application control does not cover {Prose.And(missing.Select(t => TypeNames[t]))}. It covers {Prose.And(covered.Select(t => TypeNames[t]))} ({Labels(enforcing)})." + GroupPolicy, Sources);
    }

    private static string Labels(IEnumerable<AppControlPolicy> policies) => string.Join(", ", policies.Select(p => p.Label).Distinct(StringComparer.Ordinal));
}
