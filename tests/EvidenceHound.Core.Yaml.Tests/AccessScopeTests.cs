using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary><c>scope/access.yaml</c>: who can do what in CISO Assistant, parsed and validated with the rest of the scope.</summary>
public class AccessScopeTests
{
    public const string AccessPath = "scope/access.yaml";

    public const string Access = """
        users:
          - email: it@example.com
            first_name: Alex
            last_name: Example
          - email: ceo@example.com
            first_name: Sam
            last_name: Example
          - email: former@example.com
            first_name: Robin
            last_name: Example
            active: false
          - email: hound-runner@example.com
            first_name: Evidence Hound
            last_name: runner
            service_account: publish
        teams:
          - name: IT team
            domain: Example Pty Ltd
            leader: it@example.com
            members: [ceo@example.com]
        roles:
          - role: approver
            domain: Example Pty Ltd
            users: [ceo@example.com]
          - role: analyst
            domain: Example Pty Ltd
            teams: [IT team]
            users: [hound-runner@example.com]
          - role: administrator
            domain: Global
            users: [it@example.com]
        """;

    [Fact]
    public void Example_access_parses_and_has_no_problems()
    {
        var parsed = Parse(Access);

        Problems(Access).ShouldBeEmpty();
        var access = parsed.Scope.Access;
        access.Users.Select(u => u.Email).ShouldBe(["it@example.com", "ceo@example.com", "former@example.com", "hound-runner@example.com"]);
        access.Users[2].Active.ShouldBeFalse();
        access.Users[3].ServiceAccount.ShouldBe("publish");
        var team = access.Teams.ShouldHaveSingleItem();
        (team.Name, team.Domain?.Value, team.Leader?.Value).ShouldBe(("IT team", "Example Pty Ltd", "it@example.com"));
        team.Members.Select(m => m.Value).ShouldBe(["ceo@example.com"]);
        access.Roles.Select(r => (r.Role.Value, r.Domain.Value)).ShouldBe([("approver", "Example Pty Ltd"), ("analyst", "Example Pty Ltd"), ("administrator", "Global")]);
    }

    [Fact]
    public void Duplicate_email_is_reported_with_line()
    {
        var access = Access.Replace("  - email: ceo@example.com", "  - email: IT@example.com", StringComparison.Ordinal);

        var problem = Problems(access).First(p => p.Code == "access.email_duplicate");

        problem.At.ShouldBe(new SourceLine(AccessPath, 5));
        problem.Message.ShouldContain("it@example.com");
        problem.Message.ShouldContain("line 2");
    }

    [Fact]
    public void Malformed_email_is_reported()
    {
        var problem = Problems(Access.Replace("email: former@example.com", "email: former-at-example.com", StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("access.email_invalid", 8));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("password_hash")]
    [InlineData("api_token")]
    public void Password_in_file_is_rejected(string key)
    {
        var access = Access.Replace("    last_name: runner", $"    last_name: runner\n    {key}: not-a-real-secret", StringComparison.Ordinal);

        var problem = Problems(access).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.secret_in_file", 15));
        problem.Message.ShouldNotContain("not-a-real-secret");
        problem.Hint.ShouldContain("invitation");
    }

    [Fact]
    public void Domain_without_approver_is_reported()
    {
        var setup = ScopeFixture.Setup + "\n  - name: Example Labs\n    description: Research.\n";
        var access = Access
            .Replace("""
                  - role: administrator
                    domain: Global
                    users: [it@example.com]

                """, "", StringComparison.Ordinal);

        var problems = Problems(access, setup);

        problems.Select(p => (p.Code, p.Message)).ShouldBe(
            [("access.domain_without_approver", "domain 'Example Labs' has no active user who can approve (approver, domain_manager or administrator, on it or on Global).")]);
    }

    [Fact]
    public void An_approver_through_a_team_or_on_global_counts_and_an_inactive_one_does_not()
    {
        var throughTeam = Access.Replace("""
              - role: approver
                domain: Example Pty Ltd
                users: [ceo@example.com]
            """, """
              - role: approver
                domain: Example Pty Ltd
                teams: [IT team]
            """, StringComparison.Ordinal).Replace("""
              - role: administrator
                domain: Global
                users: [it@example.com]
            """, "", StringComparison.Ordinal);
        var inactive = throughTeam.Replace("members: [ceo@example.com]", "members: [former@example.com]", StringComparison.Ordinal)
            .Replace("    leader: it@example.com\n", "", StringComparison.Ordinal);

        Problems(throughTeam).ShouldBeEmpty();
        Problems(inactive).Select(p => p.Code).ShouldBe(["access.domain_without_approver"]);
    }

    [Fact]
    public void Unknown_role_domain_user_and_team_are_reported()
    {
        var access = Access
            .Replace("role: approver", "role: superuser", StringComparison.Ordinal)
            .Replace("domain: Global", "domain: Example Holdings", StringComparison.Ordinal)
            .Replace("members: [ceo@example.com]", "members: [cfo@example.com]", StringComparison.Ordinal)
            .Replace("teams: [IT team]", "teams: [Ops team]", StringComparison.Ordinal);

        var codes = Problems(access).Select(p => p.Code).ToArray();

        codes.ShouldContain("access.role_unknown");
        codes.ShouldContain("access.domain_unknown");
        codes.ShouldContain("access.user_unknown");
        codes.ShouldContain("access.team_unknown");
    }

    [Fact]
    public void A_custom_role_is_known_once_declared()
    {
        var access = Access.Replace("role: analyst", "role: Evidence reviewer", StringComparison.Ordinal);

        Problems(access).Select(p => p.Code).ShouldBe(["access.role_unknown"]);
        Problems("custom_roles: [Evidence reviewer]\n" + access).ShouldBeEmpty();
    }

    private static ScopeParse Parse(string access, string setup = ScopeFixture.Setup) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            access: new ScopeSource(AccessPath, access));

    private static IReadOnlyList<ScopeProblem> Problems(string access, string setup = ScopeFixture.Setup)
    {
        var parsed = Parse(access, setup);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, [])];
    }
}
