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

/// <summary>A team on a domain: its leader and members, by email.</summary>
public sealed record ScopeTeam(string Name, string? Description, ScopeRef? Domain, ScopeRef? Leader, IReadOnlyList<ScopeRef> Members, SourceLine At);

/// <summary>A role on a domain, given to users and to every member (and the leader) of teams.</summary>
public sealed record ScopeRoleGrant(ScopeRef Role, ScopeRef Domain, IReadOnlyList<ScopeRef> Users, IReadOnlyList<ScopeRef> Teams, SourceLine At);
