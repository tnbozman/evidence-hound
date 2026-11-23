namespace EvidenceHound.Core.Scope;

/// <summary>
/// <c>scope/access.yaml</c>: the people who use CISO Assistant, the teams they form and the roles they hold on each domain. Never passwords:
/// people set their own through CISO Assistant's invitation.
/// </summary>
public sealed record ScopeAccess(IReadOnlyList<ScopeUser> Users, IReadOnlyList<ScopeTeam> Teams, IReadOnlyList<ScopeRoleGrant> Roles)
{
    public static ScopeAccess None { get; } = new([], [], []);

    /// <summary>Roles the organisation created in CISO Assistant, by name, beside the built-in ones.</summary>
    public IReadOnlyList<ScopeRef> CustomRoles { get; init; } = [];
}

/// <summary>A user. <see cref="ServiceAccount"/> names what a non-person account is for, such as <c>publish</c>.</summary>
public sealed record ScopeUser(string Email, string? FirstName, string? LastName, bool Active, string? ServiceAccount, SourceLine At);

/// <summary>
/// A team on a domain: its leader and members, by email. A team with an <see cref="Email"/> and no people is an owner contact: CISO
/// Assistant emails it task reminders, and it has no login.
/// </summary>
public sealed record ScopeTeam(string Name, string? Description, ScopeRef? Domain, ScopeRef? Leader, IReadOnlyList<ScopeRef> Members, SourceLine At)
{
    /// <summary>CISO Assistant's team email, which receives the team's task reminders.</summary>
    public string? Email { get; init; }

    /// <summary>A team with an email and no leader or members: someone CISO Assistant reminds, who never signs in.</summary>
    public bool IsOwnerContact => Email is not null && Leader is null && Members.Count == 0;
}

/// <summary>A role on a domain, given to users and to every member (and the leader) of teams.</summary>
public sealed record ScopeRoleGrant(ScopeRef Role, ScopeRef Domain, IReadOnlyList<ScopeRef> Users, IReadOnlyList<ScopeRef> Teams, SourceLine At);

/// <summary>One role a user holds on one domain, directly or as a team's leader or member: a role assignment in CISO Assistant.</summary>
public sealed record AccessAssignment(string RoleCode, string Domain, string Email);

/// <summary>CISO Assistant's built-in roles by the names <c>access.yaml</c> uses, and how a grant expands into one assignment per user. Pure.</summary>
public static class AccessRoles
{
    /// <summary>The built-in roles (v4.3.4), by name.</summary>
    public static IReadOnlyDictionary<string, string> Builtin { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["administrator"] = "BI-RL-ADM",
        ["domain_manager"] = "BI-RL-DMA",
        ["analyst"] = "BI-RL-ANA",
        ["approver"] = "BI-RL-APP",
        ["auditor"] = "BI-RL-AUD",
        ["baseline_reader"] = "BI-RL-BSL",
    };

    /// <summary>Roles that can approve: an approver, a domain manager or an administrator.</summary>
    public static IReadOnlySet<string> Approving { get; } = new HashSet<string>(StringComparer.Ordinal) { "BI-RL-APP", "BI-RL-DMA", "BI-RL-ADM" };

    /// <summary>A built-in role's code, or a custom role's own name.</summary>
    public static string Code(string role) => Builtin.GetValueOrDefault(role) ?? role;

    /// <summary>
    /// Every assignment the grants make: one per user, role and domain, a team's grant going to its leader and each member. Grants naming an
    /// undeclared team give nothing; validation reports them.
    /// </summary>
    public static IReadOnlyList<AccessAssignment> Assignments(ScopeAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var teams = access.Teams.GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return
        [
            .. access.Roles.SelectMany(grant => grant.Users.Select(u => u.Value)
                    .Concat(grant.Teams.SelectMany(t => teams.TryGetValue(t.Value, out var team) ? People(team) : []))
                    .Select(email => new AccessAssignment(Code(grant.Role.Value), grant.Domain.Value, email)))
                .Distinct(),
        ];
    }

    /// <summary>A team's leader and members.</summary>
    public static IEnumerable<string> People(ScopeTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return team.Leader is { } leader ? [leader.Value, .. team.Members.Select(m => m.Value)] : team.Members.Select(m => m.Value);
    }
}
