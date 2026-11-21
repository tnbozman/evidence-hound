namespace EvidenceHound.Collectors.M365;

/// <summary>Multi-factor authentication checks: who can do MFA, and which Conditional Access policies make them.</summary>
internal static class MfaChecks
{
    public static Finding RegisteredAllUsers(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding EnforcedByConditionalAccess(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding LegacyAuthBlocked(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding PhishingResistantAdmins(TenantData tenant, M365Options options) => throw new NotImplementedException();
}
