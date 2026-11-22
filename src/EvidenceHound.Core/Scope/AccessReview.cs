namespace EvidenceHound.Core.Scope;

public enum AccessIssue
{
    /// <summary>An active person has not turned on multi-factor authentication. CISO Assistant's API cannot require it per user, only report it.</summary>
    MfaOff,

    /// <summary>A service account holds a role beyond what its purpose needs.</summary>
    ServiceAccountExcessRole,
}

/// <summary>One thing the reviewer of a scope plan should know about a user's access.</summary>
public sealed record AccessNote(string Email, AccessIssue Issue, string Detail);

/// <summary>What a scope plan reports about access, from the declared users and what CISO Assistant holds. Pure.</summary>
public static class AccessReview
{
    /// <summary>Roles a publishing service account never needs: it writes evidence, applied controls and results as an analyst.</summary>
    private static readonly string[] BeyondPublishing = ["BI-RL-ADM", "BI-RL-DMA", "BI-RL-APP"];

    /// <summary>
    /// Active people, declared and already in CISO Assistant, without MFA; and each role a publishing service account (<c>service_account: publish</c>
    /// or <c>true</c>) holds beyond analyst, directly or through a user group. An <c>apply</c> service account administers the scope, so any role is expected.
    /// </summary>
    public static IReadOnlyList<AccessNote> Notes(ScopeAccess declared, IReadOnlyList<UserAccess> held)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(held);
        var users = declared.Users.ToDictionary(u => u.Email, StringComparer.Ordinal);
        return
        [
            .. held.Where(h => users.TryGetValue(h.Email, out var user) && user.Active && user.ServiceAccount is null && !h.MfaEnabled)
                .Select(h => new AccessNote(h.Email, AccessIssue.MfaOff, "has not turned on multi-factor authentication")),
            .. held.Where(h => users.TryGetValue(h.Email, out var user) && user.ServiceAccount is "publish" or "true")
                .SelectMany(h => h.Roles.Where(r => BeyondPublishing.Any(code => r.StartsWith(code + " ", StringComparison.Ordinal))).Select(role => new AccessNote(
                    h.Email,
                    AccessIssue.ServiceAccountExcessRole,
                    $"holds {role}; a publishing service account needs only analyst on the domains it publishes to"))),
        ];
    }
}
