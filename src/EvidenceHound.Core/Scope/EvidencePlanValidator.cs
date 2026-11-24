namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks <c>scope/evidence-plan.yaml</c> against the rest of the scope and the deployment: its controls and teams exist, its collectors
/// are configured, and an owner contact never holds a role, since it has no login. Pure.
/// </summary>
public static class EvidencePlanValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var teams = scope.Access.Teams.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var controls = scope.Controls.Select(c => c.RefId).ToHashSet(StringComparer.Ordinal);
        var rows = scope.EvidencePlan?.Rows ?? [];
        var named = rows.SelectMany(r => new[] { r.Owner, r.GrcTeam }).Append(scope.EvidencePlan?.GrcTeam).OfType<ScopeRef>().DistinctBy(r => (r.Value, r.At));
        return
        [
            .. named.Where(t => !teams.Contains(t.Value)).Select(t => new ScopeProblem(
                "evidence_plan.team_unknown", t.At, $"team '{t.Value}' is not declared.", "Declare it under 'teams' in scope/access.yaml; a system owner without a login is a team with an email and no members.")),
            .. rows.Select(r => r.Control).Where(c => !controls.Contains(c.Value)).Select(c => new ScopeProblem(
                "evidence_plan.control_unknown", c.At, $"control '{c.Value}' is not declared.", "Declare it in scope/controls.yaml, or correct the ref ID.")),
            .. OwnerContactRoles(scope.Access),
        ];
    }

    /// <summary>Each row's collector must be an instance in the deployment config (<paramref name="configured"/>, by name).</summary>
    public static IEnumerable<ScopeProblem> Collectors(ScopeDocument scope, IReadOnlyCollection<string> configured)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(configured);
        return (scope.EvidencePlan?.Rows ?? [])
            .Select(r => r.Collector)
            .OfType<ScopeRef>()
            .Where(c => !configured.Contains(c.Value, StringComparer.Ordinal))
            .Select(c => new ScopeProblem(
                "evidence_plan.collector_unknown",
                c.At,
                $"collector '{c.Value}' is not configured.",
                configured.Count == 0 ? "Add the collector to hound.yaml first." : $"Use one of the collectors in hound.yaml: {string.Join(", ", configured)}."));
    }

    private static IEnumerable<ScopeProblem> OwnerContactRoles(ScopeAccess access)
    {
        var contacts = access.Teams.Where(t => t.IsOwnerContact).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        return access.Roles.SelectMany(r => r.Teams).Where(t => contacts.Contains(t.Value)).Select(t => new ScopeProblem(
            "evidence_plan.owner_contact_has_role",
            t.At,
            $"team '{t.Value}' is an owner contact (an email and no people), so it has no login and cannot hold a role.",
            "Remove the team from this role. Owners send their evidence to the GRC team, who record it in CISO Assistant."));
    }
}
