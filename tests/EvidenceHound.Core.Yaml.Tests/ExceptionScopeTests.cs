using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-03: security exceptions in <c>scope/exceptions.yaml</c> and risk acceptance requests, approved only in CISO Assistant.</summary>
public class ExceptionScopeTests
{
    public const string Exceptions = """
        exceptions:
          - ref_id: EXC-01
            name: Legacy finance app without MFA
            description: The finance app cannot use single sign-on until its upgrade.
            domain: Example Pty Ltd
            severity: medium
            owners: [it@example.com]
            expiration_date: "2027-03-31"
            observation: Sign-in is limited to the office network.
            systems: [Example 365]
            controls: [AC-MFA]
            scenarios: [EX-RISK-2026/R-01]
            requirements:
              - audit: EXAMPLE-BASELINE-2026
                requirement: EX-1.1
        """;

    private static readonly DateOnly Today = new(2026, 11, 25);

    public static ScopeParse Parse(string exceptions = Exceptions, string study = RiskFixture.Study) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            risks: [new ScopeSource(RiskFixture.Path, study)],
            exceptions: new ScopeSource("scope/exceptions.yaml", exceptions));

    private static IReadOnlyList<ScopeProblem> Problems(string exceptions = Exceptions)
    {
        var parsed = Parse(exceptions);
        return [.. parsed.Problems, .. RiskValidator.Validate(parsed.Scope, RiskFixture.Libraries)];
    }

    [Fact]
    public void Exceptions_are_read_and_their_links_checked()
    {
        var exception = Parse().Scope.Exceptions.ShouldHaveSingleItem();

        (exception.RefId, exception.Severity, exception.ExpirationDate).ShouldBe(("EXC-01", "medium", "2027-03-31"));
        exception.Requirements.ShouldHaveSingleItem().ShouldSatisfyAllConditions(r => r.Audit.ShouldBe("EXAMPLE-BASELINE-2026"), r => r.Requirement.ShouldBe("EX-1.1"));
        Problems().ShouldBeEmpty();
        Problems(Exceptions.Replace("[EX-RISK-2026/R-01]", "[EX-RISK-2026/R-99]", StringComparison.Ordinal).Replace("[AC-MFA]", "[AC-NONE]", StringComparison.Ordinal))
            .Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["exception.control_unknown", "exception.scenario_unknown"]);
    }

    [Fact]
    public void Approval_status_in_file_is_rejected()
    {
        var problems = Parse(Exceptions.Replace("    severity: medium", "    severity: medium\n    status: approved", StringComparison.Ordinal)).Problems;

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("scope.approval_not_configurable");
        problem.Hint.ShouldContain("approver in CISO Assistant");
        Parse(study: RiskFixture.Study.Replace("treatment: accept", "treatment: accept\n    approved: true", StringComparison.Ordinal)).Problems
            .ShouldHaveSingleItem().Code.ShouldBe("scope.key_unknown", "an acceptance is requested by treatment: accept and approved in CISO Assistant");
    }

    [Fact]
    public void Expiring_within_60_days_is_listed()
    {
        var held = Held("EXC-01", "approved", "2027-01-20");

        var note = ExceptionReview.Notes([held, Held("EXC-02", "approved", "2027-06-30")], Today).Where(n => n.Code != "exception.status").ShouldHaveSingleItem();

        (note.Code, note.RefId, note.Expires).ShouldBe(("exception.expiring", "EXC-01", "2027-01-20"));
        note.Detail.ShouldContain("56 days");
    }

    [Fact]
    public void Expired_but_approved_is_reported()
    {
        var notes = ExceptionReview.Notes([Held("EXC-01", "approved", "2026-10-31"), Held("EXC-02", "expired", "2026-10-31")], Today);

        notes.Where(n => n.Code == "exception.expired_but_approved").ShouldHaveSingleItem().RefId.ShouldBe("EXC-01");
        notes.Where(n => n.Code == "exception.status").Select(n => (n.RefId, n.Status)).ShouldBe([("EXC-01", "approved"), ("EXC-02", "expired")]);
    }

    [Fact]
    public void Accepted_scenario_asks_for_a_risk_acceptance_and_exceptions_link_their_scenarios()
    {
        var declared = ScopeDesired.From(Parse().Scope, ScopeInputs.None with { Libraries = RiskFixture.Libraries });

        var acceptance = declared.Single(o => o.Kind == ScopeKind.RiskAcceptance);
        acceptance.Key.ShouldBe("EX-RISK-2026/R-02");
        acceptance.Fields["scenario"].ShouldBe("EX-RISK-2026/R-02");
        acceptance.Fields.Keys.ShouldNotContain("state", "CISO Assistant's approver decides");
        var exception = declared.Single(o => o.Kind == ScopeKind.SecurityException);
        exception.Fields["requirements"].ShouldBe("EXAMPLE-BASELINE-2026/EX-1.1");
        exception.Fields.Keys.ShouldNotContain("status");
        declared.Single(o => o.Key == "EX-RISK-2026/R-01" && o.Kind == ScopeKind.RiskScenario).Fields["exceptions"].ShouldBe("EXC-01");
    }

    [Fact]
    public void Exceptions_alone_ask_the_reader_for_risks()
    {
        var scope = Parse().Scope with { RiskStudies = [] };

        ScopeStateQuery.For(scope, ScopeDesired.From(scope, ScopeInputs.None)).Risks.ShouldBeTrue("exceptions and acceptances are read with the risks");
    }

    private static ActualObject Held(string refId, string status, string expires) =>
        new(new ScopeObject(ScopeKind.SecurityException, refId, new Dictionary<string, string> { ["name"] = refId }), null)
        {
            Observed = new Dictionary<string, string> { ["status"] = status, ["expiration_date"] = expires },
        };
}
