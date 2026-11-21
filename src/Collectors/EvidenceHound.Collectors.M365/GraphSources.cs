namespace EvidenceHound.Collectors.M365;

/// <summary>A Graph collection the collector reads. Its name is also its evidence file, <c>&lt;name&gt;.json</c>.</summary>
internal sealed record GraphSource(string Name, GraphRequest Request)
{
    public string EvidenceFile => Name + ".json";
}

/// <summary>Every Graph collection the collector reads, in order, with the read-only application permission each needs.</summary>
internal static class GraphSources
{
    public const string Users = "users";
    public const string MfaRegistration = "mfa-registration";
    public const string ConditionalAccess = "conditional-access-policies";

    public static IReadOnlyList<GraphSource> All { get; } =
    [
        new(Users, new("v1.0/users?$select=id,userPrincipalName,displayName,accountEnabled,userType,onPremisesSyncEnabled", "User.Read.All")),
        new(MfaRegistration, new("v1.0/reports/authenticationMethods/userRegistrationDetails", "AuditLog.Read.All")),
        new(ConditionalAccess, new("v1.0/identity/conditionalAccess/policies", "Policy.Read.All")),
    ];
}
