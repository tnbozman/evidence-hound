using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>Privileged access checks: how many Global Administrators, whether admins are cloud-only, and whether roles are eligible rather than permanent.</summary>
internal static class PrivilegedChecks
{
    public static Finding GlobalAdminCount(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "identity.privileged.global_admin_count";
        var active = Holders(tenant.Assignments, [PrivilegedRoles.GlobalAdministrator]);
        var eligible = Holders(tenant.Eligibilities ?? [], [PrivilegedRoles.GlobalAdministrator]).Except(active, StringComparer.OrdinalIgnoreCase).ToArray();
        var count = active.Count + eligible.Length;
        var holds = count == 1 ? "1 account holds" : $"{count} accounts hold";
        var observation = $"{holds} Global Administrator ({active.Count} active, {eligible.Length} eligible only); expected {options.GlobalAdminMin} to {options.GlobalAdminMax}.";
        var outcome = count >= options.GlobalAdminMin && count <= options.GlobalAdminMax ? CheckOutcome.Pass : CheckOutcome.Fail;
        return new(id, outcome, observation, tenant.HasPim ? [GraphSources.RoleAssignments, GraphSources.RoleEligibility] : [GraphSources.RoleAssignments]);
    }

    public static Finding AdminsCloudOnly(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "identity.privileged.admins_cloud_only";
        string[] sources = tenant.HasPim
            ? [GraphSources.Users, GraphSources.RoleAssignments, GraphSources.RoleEligibility]
            : [GraphSources.Users, GraphSources.RoleAssignments];
        var users = tenant.Users.ToDictionary(u => u.Id, StringComparer.OrdinalIgnoreCase);
        var holders = Holders(tenant.Assignments.Concat(tenant.Eligibilities ?? []), options.PrivilegedRoles);
        var accounts = holders.Where(users.ContainsKey).Select(h => users[h]).ToArray();
        var others = holders.Count - accounts.Length;
        if (accounts.Length == 0)
        {
            return new(id, CheckOutcome.Fail, "No user accounts hold privileged roles, so none could be assessed.", sources);
        }

        var synced = accounts.Where(a => a.Synced).Select(a => a.Id).ToArray();
        if (synced.Length > 0)
        {
            var verb = synced.Length == 1 ? "is" : "are";
            return new(id, CheckOutcome.Fail, $"{synced.Length} of {accounts.Length} accounts holding privileged roles {verb} synchronised from on-premises: {string.Join(", ", synced)}.", sources);
        }

        var note = others switch
        {
            0 => string.Empty,
            1 => " 1 privileged role holder is a group or app, not assessed.",
            _ => $" {others} privileged role holders are groups or apps, not assessed.",
        };
        return new(id, CheckOutcome.Pass, $"All {accounts.Length} accounts holding privileged roles are cloud-only.{note}", sources);
    }

    public static Finding PimEligibleNotPermanent(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        const string id = "identity.privileged.pim_eligible_not_permanent";
        if (tenant is not { Instances: { } instances, Eligibilities: { } eligibilities })
        {
            return new(
                id,
                CheckOutcome.NotApplicable,
                "Privileged Identity Management is not licensed in this tenant (it needs Microsoft Entra ID P2 or ID Governance), so roles cannot be made eligible.",
                [GraphSources.RoleAssignments]);
        }

        string[] sources = [GraphSources.RoleInstances, GraphSources.RoleEligibility];
        var permanent = instances.Where(i => i.IsPermanentAssignment && IsPrivileged(i, options.PrivilegedRoles)).ToArray();
        var breakGlass = permanent.Where(i => options.BreakGlass.Contains(i.PrincipalId)).Select(i => i.PrincipalId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var others = permanent.Where(i => !options.BreakGlass.Contains(i.PrincipalId)).ToArray();
        if (others.Length > 0)
        {
            var what = others.Length == 1 ? "1 privileged role assignment is" : $"{others.Length} privileged role assignments are";
            var list = string.Join(", ", others.Select(i => $"{PrivilegedRoles.NameOf(i.RoleId)} ({i.PrincipalId})"));
            return new(id, CheckOutcome.Fail, $"{what} permanent rather than eligible: {list}.", sources);
        }

        var eligible = eligibilities.Count(e => IsPrivileged(e, options.PrivilegedRoles));
        var apart = breakGlass == 0 ? string.Empty : $" apart from the {breakGlass} documented break-glass accounts";
        return new(id, CheckOutcome.Pass, $"No permanent privileged role assignments{apart}; {eligible} privileged role assignments are eligible through PIM.", sources);
    }

    private static IReadOnlyList<string> Holders(IEnumerable<RoleGrant> grants, IReadOnlyList<string> roles) =>
        [.. grants.Where(g => IsPrivileged(g, roles)).Select(g => g.PrincipalId).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool IsPrivileged(RoleGrant grant, IReadOnlyList<string> roles) => roles.Contains(grant.RoleId, StringComparer.OrdinalIgnoreCase);
}
