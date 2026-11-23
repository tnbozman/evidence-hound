namespace EvidenceHound.Collectors.M365;

/// <summary>
/// A Graph collection the collector reads. Its name is also its evidence file, <c>&lt;name&gt;.json</c>. A collection that
/// <paramref name="NeedsLicence"/> may be missing from a tenant without that licence; the run goes on without it.
/// </summary>
internal sealed record GraphSource(string Name, GraphRequest Request, bool NeedsLicence = false)
{
    /// <summary>The tenant may refuse it, for a missing licence or permission; the run goes on without it and its checks say why.</summary>
    public bool MayBeRefused { get; init; }

    public string EvidenceFile => Name + ".json";
}

/// <summary>Every Graph collection the collector reads, in order, with the read-only application permission each needs.</summary>
internal static class GraphSources
{
    public const string Users = "users";
    public const string MfaRegistration = "mfa-registration";
    public const string ConditionalAccess = "conditional-access-policies";
    public const string RoleAssignments = "role-assignments";
    public const string RoleEligibility = "role-eligibility-schedules";
    public const string RoleInstances = "role-assignment-schedule-instances";
    public const string ManagedDevices = "managed-devices";
    public const string DeviceConfigurations = "device-configurations";
    public const string ConfigurationPolicies = "configuration-policies";
    public const string AppControlEvents = "hunting-app-control-events";
    public const string BlockedFiles = "hunting-app-control-blocked-files";
    public const string DefenderOnboarding = "hunting-defender-onboarding";

    private static readonly string HuntingPath = "v1.0/security/runHuntingQuery";

    private static readonly string HuntingPermission = "ThreatHunting.Read.All";

    private static readonly string AppControlActions = "(ActionType startswith \"AppControl\" or ActionType startswith \"AppLocker\")";

    public static IReadOnlyList<GraphSource> All { get; } =
    [
        new(Users, new("v1.0/users?$select=id,userPrincipalName,displayName,accountEnabled,userType,onPremisesSyncEnabled", "User.Read.All")),
        new(MfaRegistration, new("v1.0/reports/authenticationMethods/userRegistrationDetails", "AuditLog.Read.All")),
        new(ConditionalAccess, new("v1.0/identity/conditionalAccess/policies", "Policy.Read.All")),
        new(RoleAssignments, new("v1.0/roleManagement/directory/roleAssignments", "RoleManagement.Read.Directory")),
        new(RoleEligibility, new("v1.0/roleManagement/directory/roleEligibilitySchedules", "RoleManagement.Read.Directory"), NeedsLicence: true),
        new(RoleInstances, new("v1.0/roleManagement/directory/roleAssignmentScheduleInstances", "RoleManagement.Read.Directory"), NeedsLicence: true),
        new(ManagedDevices, new("v1.0/deviceManagement/managedDevices?$select=id,deviceName,operatingSystem,osVersion,complianceState,userPrincipalName", "DeviceManagementManagedDevices.Read.All")),
        new(DeviceConfigurations, new("v1.0/deviceManagement/deviceConfigurations?$expand=assignments", "DeviceManagementConfiguration.Read.All")),

        // Settings catalog policies (where Intune keeps Office and attack surface reduction settings) exist only in the Graph beta API.
        new(ConfigurationPolicies, new("beta/deviceManagement/configurationPolicies?$expand=settings,assignments", "DeviceManagementConfiguration.Read.All")),
    ];

    /// <summary>
    /// The advanced hunting queries over the last <paramref name="days"/> days (at most 30, what advanced hunting keeps): application
    /// control events per device and action, the most-blocked files, and Defender for Endpoint onboarding of Windows devices. Each
    /// query starts with a marker comment naming it. They return counts, file names and hashes only: no user or device names.
    /// </summary>
    public static IReadOnlyList<GraphSource> Hunting(int days) =>
    [
        Hunt(AppControlEvents, days, $$"""
            DeviceEvents
            | where Timestamp > ago({{days}}d)
            | where {{AppControlActions}}
            | summarize Events = count(), LastSeen = max(Timestamp) by DeviceId, ActionType
            """),
        Hunt(BlockedFiles, days, $$"""
            DeviceEvents
            | where Timestamp > ago({{days}}d)
            | where {{AppControlActions}} and ActionType contains "Block"
            | extend Publisher = tostring(parse_json(AdditionalFields).Publisher)
            | summarize Blocks = count(), Devices = dcount(DeviceId) by FileName, SHA256, Publisher
            | top 20 by Blocks
            """),
        Hunt(DefenderOnboarding, days, """
            DeviceInfo
            | where Timestamp > ago(7d)
            | where OSPlatform startswith "Windows"
            | summarize arg_max(Timestamp, OnboardingStatus) by DeviceId
            | summarize Devices = count() by OnboardingStatus
            """),
    ];

    /// <summary>Every source for these options: the collections, then the hunting queries.</summary>
    public static IReadOnlyList<GraphSource> For(M365Options options) => [.. All, .. Hunting(options.ApplicationControlWindowDays)];

    private static GraphSource Hunt(string name, int days, string query) =>
        new(name, new GraphRequest(HuntingPath, HuntingPermission)
        {
            Body = new System.Text.Json.Nodes.JsonObject
            {
                ["Query"] = $"// evidence-hound: {name}\n{query}",
                ["Timespan"] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"P{days}D"),
            },
            ItemsProperty = "results",
        })
        { MayBeRefused = true };
}
