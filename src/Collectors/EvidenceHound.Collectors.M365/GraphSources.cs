namespace EvidenceHound.Collectors.M365;

/// <summary>
/// A Graph collection the collector reads. Its name is also its evidence file, <c>&lt;name&gt;.json</c>. A collection that
/// <paramref name="NeedsLicence"/> may be missing from a tenant without that licence; the run goes on without it.
/// </summary>
internal sealed record GraphSource(string Name, GraphRequest Request, bool NeedsLicence = false)
{
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
}
