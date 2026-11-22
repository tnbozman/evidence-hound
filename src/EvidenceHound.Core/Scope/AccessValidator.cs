namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks <c>scope/access.yaml</c> against the rest of the scope: roles, domains, users and teams it names must exist, and every domain needs an
/// active user who can approve, on it or on the root folder. Pure.
/// </summary>
internal static class AccessValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope)
    {
        var access = scope.Access;
        if (access.Users.Count == 0 && access.Teams.Count == 0 && access.Roles.Count == 0)
        {
            return [];
        }

        var root = scope.Setup.RootFolder;
        var domains = scope.Setup.Domains.Select(d => d.Name).Append(root).ToHashSet(StringComparer.Ordinal);
        var users = access.Users.Select(u => u.Email).ToHashSet(StringComparer.Ordinal);
        var teams = access.Teams.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var roles = AccessRoles.Builtin.Keys.Concat(access.CustomRoles.Select(r => r.Value)).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. access.Teams.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).Select(t => new ScopeProblem(
                "scope.duplicate", t.At, $"team '{t.Name}' is declared more than once.", "Give each team its own name, or remove the copy.")),
            .. access.Teams.Select(t => t.Domain).OfType<ScopeRef>().Concat(access.Roles.Select(r => r.Domain)).Where(d => !domains.Contains(d.Value)).Select(d => new ScopeProblem(
                "access.domain_unknown", d.At, $"domain '{d.Value}' is not declared.", $"Declare it in scope/setup.yaml, or use '{root}' for the whole organisation.")),
            .. access.Teams.SelectMany(t => t.Leader is { } leader ? [leader, .. t.Members] : t.Members)
                .Concat(access.Roles.SelectMany(r => r.Users))
                .Where(u => !users.Contains(u.Value))
                .Select(u => new ScopeProblem("access.user_unknown", u.At, $"user '{u.Value}' is not listed under 'users'.", "Add them under 'users', or correct the address.")),
            .. access.Roles.SelectMany(r => r.Teams).Where(t => !teams.Contains(t.Value)).Select(t => new ScopeProblem(
                "access.team_unknown", t.At, $"team '{t.Value}' is not declared.", "Declare it under 'teams', or correct the name.")),
            .. access.Roles.Select(r => r.Role).Where(r => !roles.Contains(r.Value)).Select(r => new ScopeProblem(
                "access.role_unknown",
                r.At,
                $"role '{r.Value}' is neither built in nor declared.",
                $"Use one of {string.Join(", ", AccessRoles.Builtin.Keys)}, or list a role you created in CISO Assistant under 'custom_roles'.")),
            .. WithoutApprover(scope, root),
        ];
    }

    private static IEnumerable<ScopeProblem> WithoutApprover(ScopeDocument scope, string root)
    {
        var active = scope.Access.Users.Where(u => u.Active).Select(u => u.Email).ToHashSet(StringComparer.Ordinal);
        var approved = AccessRoles.Assignments(scope.Access)
            .Where(a => AccessRoles.Approving.Contains(a.RoleCode) && active.Contains(a.Email))
            .Select(a => a.Domain)
            .ToHashSet(StringComparer.Ordinal);
        return approved.Contains(root)
            ? []
            : scope.Setup.Domains.Where(d => !approved.Contains(d.Name)).Select(d => new ScopeProblem(
                "access.domain_without_approver",
                d.At,
                $"domain '{d.Name}' has no active user who can approve (approver, domain_manager or administrator, on it or on {root}).",
                $"Give an active user or team the approver role on '{d.Name}', or a wider role on {root}, in scope/access.yaml."));
    }
}
