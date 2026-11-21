namespace EvidenceHound.Collectors.M365;

/// <summary>One check the collector reports: its ID and the pure function that evaluates it.</summary>
internal sealed record M365Check(string Id, Func<TenantData, M365Options, Finding> Evaluate);

/// <summary>Every check, in the order they are reported.</summary>
internal static class M365Checks
{
    public static IReadOnlyList<M365Check> All { get; } =
    [
        new("identity.mfa.registered_all_users", MfaChecks.RegisteredAllUsers),
        new("identity.mfa.enforced_conditional_access", MfaChecks.EnforcedByConditionalAccess),
        new("identity.legacy_auth_blocked", MfaChecks.LegacyAuthBlocked),
        new("identity.mfa.phishing_resistant_admins", MfaChecks.PhishingResistantAdmins),
    ];
}
