using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>Multi-factor authentication checks: who can do MFA, and which Conditional Access policies make them.</summary>
internal static class MfaChecks
{
    /// <summary>The built-in "Phishing-resistant MFA" authentication strength, the same in every tenant.</summary>
    public const string PhishingResistantStrength = "00000000-0000-0000-0000-000000000004";

    public static Finding RegisteredAllUsers(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "identity.mfa.registered_all_users";
        string[] sources = [GraphSources.Users, GraphSources.MfaRegistration];
        var members = tenant.Users.Where(u => u.Enabled && u.IsMember).ToArray();
        if (members.Length == 0)
        {
            return new(id, CheckOutcome.Fail, "No enabled member accounts were found in the directory.", sources);
        }

        var capable = tenant.Registrations.Where(r => r.IsMfaCapable).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = members.Count(m => capable.Contains(m.Id));
        if (count == members.Length)
        {
            return new(id, CheckOutcome.Pass, $"All {members.Length} enabled member accounts are MFA-capable.", sources);
        }

        var percent = 100.0 * count / members.Length;
        var missing = members.Length - count;
        var observation = string.Create(
            CultureInfo.InvariantCulture,
            $"{count} of {members.Length} enabled member accounts ({percent:0.#} %) are MFA-capable; {missing} {(missing == 1 ? "is" : "are")} not. Partial at {options.MfaPartialPercent:0.#} % or more.");
        return new(id, percent >= options.MfaPartialPercent ? CheckOutcome.Partial : CheckOutcome.Fail, observation, sources);
    }

    public static Finding EnforcedByConditionalAccess(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        const string id = "identity.mfa.enforced_conditional_access";
        var candidates = tenant.Policies.Where(p => p.IsEnabled && p.ForAllUsers && p.ForAllApps && RequiresMfa(p));
        return Best(
            id,
            candidates,
            options,
            (policy, exclusions) => $"Policy '{policy.Name}' requires MFA for all users and all cloud apps, {exclusions}.",
            (policy, undocumented) => $"Policy '{policy.Name}' requires MFA for all users but also excludes {undocumented}.",
            "No enabled Conditional Access policy requires MFA for all users and all cloud apps.");
    }

    public static Finding LegacyAuthBlocked(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        const string id = "identity.legacy_auth_blocked";
        var candidates = tenant.Policies.Where(p =>
            p.IsEnabled && p.ForAllUsers && p.ForAllApps
            && p.ClientAppTypes.Contains("exchangeActiveSync", StringComparer.OrdinalIgnoreCase)
            && p.ClientAppTypes.Contains("other", StringComparer.OrdinalIgnoreCase)
            && p.BuiltInControls.Contains("block", StringComparer.OrdinalIgnoreCase));
        return Best(
            id,
            candidates,
            options,
            (policy, exclusions) => $"Policy '{policy.Name}' blocks Exchange ActiveSync and other legacy clients for all users, {exclusions}.",
            (policy, undocumented) => $"Policy '{policy.Name}' blocks legacy authentication but also excludes {undocumented}.",
            "No enabled Conditional Access policy blocks legacy authentication clients (Exchange ActiveSync and other clients) for all users.");
    }

    public static Finding PhishingResistantAdmins(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "identity.mfa.phishing_resistant_admins";
        string[] sources = [GraphSources.ConditionalAccess];
        var policies = tenant.Policies
            .Where(p => p.IsEnabled && p.ForAllApps && string.Equals(p.AuthenticationStrengthId, PhishingResistantStrength, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var roles = options.PrivilegedRoles;
        var covered = roles.Where(role => policies.Any(p =>
            (p.ForAllUsers || p.IncludeRoles.Contains(role, StringComparer.OrdinalIgnoreCase)) && !p.ExcludeRoles.Contains(role, StringComparer.OrdinalIgnoreCase))).ToArray();

        if (covered.Length == 0)
        {
            return new(id, CheckOutcome.Fail, "No enabled Conditional Access policy requires a phishing-resistant authentication strength for privileged roles.", sources);
        }

        if (covered.Length == roles.Count)
        {
            return new(id, CheckOutcome.Pass, $"A phishing-resistant authentication strength is required for all {roles.Count} privileged roles.", sources);
        }

        var uncovered = string.Join(", ", roles.Except(covered, StringComparer.OrdinalIgnoreCase).Select(PrivilegedRoles.NameOf));
        return new(id, CheckOutcome.Partial, $"A phishing-resistant authentication strength is required for {covered.Length} of {roles.Count} privileged roles. Not covered: {uncovered}.", sources);
    }

    /// <summary>
    /// Passes on the first candidate policy whose only exclusions are documented break-glass accounts; fails naming the
    /// undocumented exclusions of the first candidate otherwise, or when there is no candidate.
    /// </summary>
    private static Finding Best(
        string id,
        IEnumerable<CaPolicy> candidates,
        M365Options options,
        Func<CaPolicy, string, string> passes,
        Func<CaPolicy, string, string> excludes,
        string none)
    {
        string[] sources = [GraphSources.ConditionalAccess];
        var assessed = candidates.Select(p => (Policy: p, Undocumented: Undocumented(p, options))).ToArray();
        if (assessed.FirstOrDefault(a => a.Undocumented.Length == 0) is { Policy: { } clean })
        {
            var documented = clean.ExcludeUsers.Count(options.BreakGlass.Contains);
            var exclusions = documented == 0 ? "with no exclusions" : $"excluding only the {documented} documented break-glass accounts";
            return new(id, CheckOutcome.Pass, passes(clean, exclusions), sources);
        }

        if (assessed.Length > 0)
        {
            var (policy, undocumented) = assessed[0];
            var what = undocumented.Length == 1 ? "1 account or group" : $"{undocumented.Length} accounts or groups";
            return new(id, CheckOutcome.Fail, excludes(policy, $"{what} not documented as break-glass: {string.Join(", ", undocumented)}"), sources);
        }

        return new(id, CheckOutcome.Fail, none, sources);
    }

    /// <summary>Excluded users that are not break-glass accounts, and every excluded group and role.</summary>
    private static string[] Undocumented(CaPolicy policy, M365Options options) =>
        [.. policy.ExcludeUsers.Where(u => !options.BreakGlass.Contains(u)), .. policy.ExcludeGroups, .. policy.ExcludeRoles];

    private static bool RequiresMfa(CaPolicy policy)
    {
        var mfa = policy.BuiltInControls.Contains("mfa", StringComparer.OrdinalIgnoreCase) || policy.AuthenticationStrengthId is not null;
        var controls = policy.BuiltInControls.Count + (policy.AuthenticationStrengthId is null ? 0 : 1);
        return mfa && (controls == 1 || string.Equals(policy.Operator, "AND", StringComparison.OrdinalIgnoreCase));
    }
}
