using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s10-04: <c>scope/evidence-plan.yaml</c> says how each control's evidence arrives, who provides it and how often, and an owner contact
/// (a team with an email and no people) is reminded by CISO Assistant without a login.
/// </summary>
public class EvidencePlanScopeTests
{
    public const string PlanPath = "scope/evidence-plan.yaml";

    public const string Access = """
        users:
          - email: it@example.com
            first_name: Alex
            last_name: Example
        teams:
          - name: GRC team
            domain: Example Pty Ltd
            leader: it@example.com
          - name: Example 365 owners
            description: The people who run Example 365. They are emailed, and have no login.
            email: owners-365@example.com
        roles:
          - role: approver
            domain: Example Pty Ltd
            users: [it@example.com]
          - role: analyst
            domain: Example Pty Ltd
            teams: [GRC team]
        """;

    public const string Plan = """
        grc_team: GRC team
        evidence:
          - ref_id: EVP-ADMIN-REVIEW
            control: AC-MFA
            requirements: [EX-1.2]
            method: manual
            owner: Example 365 owners
            every: 3 months
            runbook: manual-sources/admin-review.md
          - ref_id: EVP-MFA
            control: AC-MFA
            requirements: [EX-1.1]
            method: automated
            collector: example-m365
        """;

    [Fact]
    public void Example_plan_parses_and_has_no_problems()
    {
        var parsed = Parse(Plan);

        Problems(Plan).ShouldBeEmpty();
        var plan = parsed.Scope.EvidencePlan.ShouldNotBeNull();
        var manual = plan.Rows[0];
        (manual.RefId, manual.Control.Value, manual.Method, manual.Owner?.Value, manual.GrcTeam?.Value, manual.EveryMonths, manual.Runbook)
            .ShouldBe(("EVP-ADMIN-REVIEW", "AC-MFA", EvidenceMethod.Manual, "Example 365 owners", "GRC team", 3, "manual-sources/admin-review.md"));
        manual.Requirements.Select(r => r.Key.ToString()).ShouldBe(["EX-1.2"]);
        (plan.Rows[1].Method, plan.Rows[1].Collector?.Value).ShouldBe((EvidenceMethod.Automated, "example-m365"));
        parsed.Scope.Access.Teams[1].Email.ShouldBe("owners-365@example.com");
    }

    [Fact]
    public void Manual_row_without_runbook_is_reported()
    {
        var plan = Plan.Replace("    runbook: manual-sources/admin-review.md\n", "", StringComparison.Ordinal)
            .Replace("    every: 3 months\n", "", StringComparison.Ordinal)
            .Replace("    owner: Example 365 owners\n", "", StringComparison.Ordinal);

        var problems = Problems(plan);

        problems.Select(p => p.Code).ShouldBe(["evidence_plan.owner_missing", "evidence_plan.every_missing", "evidence_plan.runbook_missing"], ignoreOrder: true);
        problems.ShouldAllBe(p => p.At == new SourceLine(PlanPath, 3));
        problems.Single(p => p.Code == "evidence_plan.runbook_missing").Hint.ShouldContain("what to export");
    }

    [Fact]
    public void Automated_row_names_configured_collector()
    {
        Problems(Plan.Replace("\n    collector: example-m365", "", StringComparison.Ordinal)).Select(p => p.Code).ShouldBe(["evidence_plan.collector_missing"]);

        var parsed = Parse(Plan);
        EvidencePlanValidator.Collectors(parsed.Scope, ["example-m365"]).ShouldBeEmpty();
        var problem = EvidencePlanValidator.Collectors(parsed.Scope, ["example-sophos"]).ShouldHaveSingleItem();
        (problem.Code, problem.At.Line).ShouldBe(("evidence_plan.collector_unknown", 14));
        problem.Message.ShouldContain("example-m365");
        problem.Hint.ShouldContain("example-sophos");
    }

    [Fact]
    public void Owner_contact_with_role_is_reported()
    {
        var access = Access + "\n  - role: auditor\n    domain: Example Pty Ltd\n    teams: [Example 365 owners]\n";

        var problem = Problems(Plan, access).ShouldHaveSingleItem();

        (problem.Code, problem.At.File).ShouldBe(("evidence_plan.owner_contact_has_role", "scope/access.yaml"));
        problem.Message.ShouldContain("Example 365 owners");
    }

    [Fact]
    public void Teams_controls_and_intervals_must_exist()
    {
        var plan = Plan.Replace("owner: Example 365 owners", "owner: Nobody", StringComparison.Ordinal)
            .Replace("grc_team: GRC team", "grc_team: Security team", StringComparison.Ordinal)
            .Replace("control: AC-MFA\n    requirements: [EX-1.1]", "control: AC-NONE\n    requirements: [EX-1.1]", StringComparison.Ordinal)
            .Replace("every: 3 months", "every: 2 months", StringComparison.Ordinal);

        Problems(plan).Select(p => p.Code).ShouldBe(
            ["scope.value_invalid", "evidence_plan.team_unknown", "evidence_plan.team_unknown", "evidence_plan.control_unknown"], ignoreOrder: true);
    }

    private static ScopeParse Parse(string plan, string access = Access) => ScopeLoader.Parse(
        new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
        new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
        [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
        new ScopeSource("scope/access.yaml", access),
        evidencePlan: new ScopeSource(PlanPath, plan));

    private static IReadOnlyList<ScopeProblem> Problems(string plan, string access = Access)
    {
        var parsed = Parse(plan, access);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, [])];
    }
}
