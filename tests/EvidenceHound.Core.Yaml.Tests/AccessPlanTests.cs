using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>How <c>scope/access.yaml</c> becomes users, teams and role assignments, and how the plan treats them.</summary>
public class AccessPlanTests
{
    [Fact]
    public void Access_declares_users_teams_and_one_role_assignment_per_user_role_and_domain()
    {
        var declared = Declared().Where(o => o.Kind is ScopeKind.User or ScopeKind.Team or ScopeKind.RoleAssignment).ToDictionary(o => (o.Kind, o.Key));

        declared.Keys.ShouldBe(
        [
            (ScopeKind.User, "it@example.com"),
            (ScopeKind.User, "ceo@example.com"),
            (ScopeKind.User, "former@example.com"),
            (ScopeKind.User, "hound-runner@example.com"),
            (ScopeKind.Team, "IT team"),
            (ScopeKind.RoleAssignment, "BI-RL-APP|Example Pty Ltd|ceo@example.com"),
            (ScopeKind.RoleAssignment, "BI-RL-ANA|Example Pty Ltd|hound-runner@example.com"),
            (ScopeKind.RoleAssignment, "BI-RL-ANA|Example Pty Ltd|it@example.com"),
            (ScopeKind.RoleAssignment, "BI-RL-ANA|Example Pty Ltd|ceo@example.com"),
            (ScopeKind.RoleAssignment, "BI-RL-ADM|Global|it@example.com"),
        ],
            ignoreOrder: true);
        declared[(ScopeKind.User, "former@example.com")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["first_name"] = "Robin",
            ["last_name"] = "Example",
            ["active"] = "false",
        });
        declared[(ScopeKind.Team, "IT team")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["domain"] = "Example Pty Ltd",
            ["leader"] = "it@example.com",
            ["members"] = "ceo@example.com",
        });
        declared[(ScopeKind.RoleAssignment, "BI-RL-ADM|Global|it@example.com")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["role"] = "BI-RL-ADM",
            ["domain"] = "Global",
            ["user"] = "it@example.com",
        });
    }

    [Fact]
    public void New_users_teams_and_roles_are_created_and_a_second_plan_is_empty()
    {
        var declared = Declared();
        var access = declared.Where(o => o.Kind is ScopeKind.User or ScopeKind.Team or ScopeKind.RoleAssignment).ToArray();

        var first = ScopePlanner.Plan(access, [], prune: false);
        var applied = first.Changes.Select(c => new ActualObject(c.Desired!, c.State)).ToArray();

        first.Changes.ShouldAllBe(c => c.Action == ScopeAction.Create);
        first.Changes.Select(c => c.Kind).Distinct().ShouldBe([ScopeKind.User, ScopeKind.Team, ScopeKind.RoleAssignment]);
        ScopePlanner.Plan(access, applied, prune: false).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Removed_user_is_deactivated_only_with_prune()
    {
        var access = Declared().Where(o => o.Kind == ScopeKind.User).ToArray();
        var applied = access.Select(Applied).ToList();
        applied.Add(Applied(new ScopeObject(ScopeKind.User, "left@example.com", Fields(("first_name", "Kim"), ("last_name", "Example"), ("active", "true")))));
        applied.Add(new ActualObject(new ScopeObject(ScopeKind.User, "admin@example.com", Fields(("active", "true"))), null));

        var kept = ScopePlanner.Plan(access, applied, prune: false);
        var pruned = ScopePlanner.Plan(access, applied, prune: true);

        kept.Changes.ShouldBeEmpty();
        kept.Drift.Select(d => (d.Key, d.Reason, d.Managed)).ShouldBe([("left@example.com", DriftReason.NotInScope, true), ("admin@example.com", DriftReason.NotInScope, false)], ignoreOrder: true);
        var change = pruned.Changes.ShouldHaveSingleItem();
        (change.Kind, change.Key, change.Action).ShouldBe((ScopeKind.User, "left@example.com", ScopeAction.Update));
        change.Fields.ShouldBe([new FieldChange("active", "true", "false")]);
        change.Desired!.Fields["active"].ShouldBe("false");
    }

    [Fact]
    public void A_deactivated_user_is_not_deactivated_again()
    {
        var access = Declared().Where(o => o.Kind == ScopeKind.User).ToArray();
        var gone = new ScopeObject(ScopeKind.User, "left@example.com", Fields(("first_name", "Kim"), ("active", "false")));

        var plan = ScopePlanner.Plan(access, [.. access.Select(Applied), Applied(gone)], prune: true);

        plan.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void A_removed_role_assignment_is_deleted_only_with_prune()
    {
        var access = Declared().Where(o => o.Kind == ScopeKind.RoleAssignment).ToArray();
        var extra = Applied(new ScopeObject(ScopeKind.RoleAssignment, "BI-RL-DMA|Example Pty Ltd|it@example.com", Fields(("role", "BI-RL-DMA"), ("domain", "Example Pty Ltd"), ("user", "it@example.com"))));

        ScopePlanner.Plan(access, [.. access.Select(Applied), extra], prune: false).Changes.ShouldBeEmpty();
        var change = ScopePlanner.Plan(access, [.. access.Select(Applied), extra], prune: true).Changes.ShouldHaveSingleItem();
        (change.Key, change.Action).ShouldBe(("BI-RL-DMA|Example Pty Ltd|it@example.com", ScopeAction.Delete));
    }

    private static IReadOnlyList<ScopeObject> Declared()
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            access: new ScopeSource(AccessScopeTests.AccessPath, AccessScopeTests.Access));
        parsed.Problems.ShouldBeEmpty();
        return ScopeDesired.From(parsed.Scope);
    }

    private static ActualObject Applied(ScopeObject declared) => new(declared, ScopeMarker.State(declared.Kind, declared.Fields));

    private static Dictionary<string, string> Fields(params (string Key, string Value)[] fields) => fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
}
