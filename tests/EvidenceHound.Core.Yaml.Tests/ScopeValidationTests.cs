using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

public class ScopeValidationTests
{
    [Fact]
    public void Example_scope_has_no_problems()
    {
        ScopeFixture.Problems(targets: ScopeFixture.Target(computedRefs: "EX-1.1")).ShouldBeEmpty();
    }

    [Fact]
    public void Undeclared_system_is_reported_with_file_and_line()
    {
        var audit = ScopeFixture.Audit.Replace("systems: [Example 365, Example laptops]", "systems: [Example 365, Example tablets]", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.reference_unknown", new SourceLine(ScopeFixture.AuditPath, 13)));
        problem.Message.ShouldContain("system 'Example tablets'");
        problem.Hint.ShouldContain("setup.yaml");
    }

    [Fact]
    public void Undeclared_domain_perimeter_framework_document_and_control_are_reported()
    {
        var audit = ScopeFixture.Audit
            .Replace("domain: Example Pty Ltd", "domain: Example Holdings", StringComparison.Ordinal)
            .Replace("perimeter: Example baseline", "perimeter: Example cloud", StringComparison.Ordinal)
            .Replace("  - ref_id: AC-MFA", "  - ref_id: AC-SSO", StringComparison.Ordinal);
        var controls = ScopeFixture.Controls.Replace("documents: [mfa-policy]", "documents: [sso-policy]", StringComparison.Ordinal);
        var setup = ScopeFixture.Setup.Replace("        parents: [Example 365]", "        parents: [Example 366]", StringComparison.Ordinal);

        var problems = ScopeFixture.Problems(setup, controls, audit);

        problems.ShouldAllBe(p => p.Code == "scope.reference_unknown");
        problems.Select(p => p.Message).ShouldBe(
            [
                "system 'Example 366' is not declared.",
                "document 'sso-policy' is not declared in any audit.",
                "domain 'Example Holdings' is not declared.",
                "perimeter 'Example cloud' is not declared in domain 'Example Holdings'.",
                "control 'AC-SSO' is not declared.",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Framework_not_in_setup_is_reported()
    {
        var audit = ScopeFixture.Audit.Replace("framework: urn:example:risk:framework:example-baseline", "framework: urn:example:risk:framework:other", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.reference_unknown", 3));
        problem.Message.ShouldBe("framework 'urn:example:risk:framework:other' is not declared.");
    }

    [Fact]
    public void Unknown_group_for_framework_is_reported()
    {
        var audit = ScopeFixture.Audit.Replace("groups: [\"1\"]", "groups: [\"1\", \"3\"]", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.group_unknown", 4));
        problem.Message.ShouldContain("'3'");
        problem.Hint.ShouldContain("1, 2");
    }

    [Fact]
    public void Requirement_not_in_framework_is_reported()
    {
        var audit = ScopeFixture.Audit.Replace("requirements: [EX-1.1, EX-1.2]", "requirements: [EX-1.1, EX-9.9]", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.requirement_unknown", 22));
        problem.Message.ShouldContain("EX-9.9");
    }

    [Fact]
    public void Control_citing_unassessed_requirement_is_reported()
    {
        var audit = ScopeFixture.Audit.Replace("requirements: [EX-1.1, EX-1.2]", "requirements: [EX-1.1, EX-2.1]", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.requirement_not_assessed", 22));
        problem.Message.ShouldContain("EX-2.1");
        problem.Hint.ShouldContain("groups");
    }

    [Fact]
    public void Hand_written_result_for_mapped_requirement_conflicts()
    {
        var problem = ScopeFixture.Problems(targets: ScopeFixture.Target(computedRefs: ["EX-1.1", "EX-1.2"])).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.result_conflict", new SourceLine(ScopeFixture.AuditPath, 24)));
        problem.Message.ShouldContain("EX-1.2");
    }

    [Fact]
    public void Hand_written_result_by_urn_conflicts_with_a_mapping_by_ref()
    {
        var audit = ScopeFixture.Audit.Replace("  - ref: EX-1.2", "  - urn: urn:example:risk:req_node:example-baseline:ex-1.2", StringComparison.Ordinal);

        ScopeFixture.Problems(audit: audit, targets: ScopeFixture.Target(computedRefs: "EX-1.2")).ShouldHaveSingleItem().Code.ShouldBe("scope.result_conflict");
    }

    [Theory]
    [InlineData("observation", "read")]
    [InlineData("result", "hidden")]
    [InlineData("evidences", "read")]
    public void Field_hound_writes_must_be_editable_by_the_auditor(string field, string access)
    {
        var audit = ScopeFixture.Audit.Replace("  observation: {auditor: edit, respondent: read}", $"  {field}: {{auditor: {access}, respondent: read}}", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.field_not_writable", 12));
        problem.Message.ShouldContain(field);
    }

    [Fact]
    public void Unlisted_is_required()
    {
        var audit = ScopeFixture.Audit.Replace("unlisted:\n  result: not_assessed", "", StringComparison.Ordinal);

        var problem = ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.unlisted_missing", new SourceLine(ScopeFixture.AuditPath, 1)));
        problem.Hint.ShouldContain("not_applicable");
    }

    [Fact]
    public void Not_applicable_unlisted_needs_a_reason()
    {
        var audit = ScopeFixture.Audit.Replace("  result: not_assessed", "  result: not_applicable", StringComparison.Ordinal);

        ScopeFixture.Problems(audit: audit).ShouldHaveSingleItem().Code.ShouldBe("scope.unlisted_reason_missing");
    }

    [Fact]
    public void Target_audit_must_be_in_scope()
    {
        var problem = ScopeFixture.Problems(targets: ScopeFixture.Target(audit: "EX-OTHER-2026")).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("config.target_not_in_scope", new SourceLine("hound.yaml", 30)));
        problem.Message.ShouldContain("EX-OTHER-2026");
    }

    [Fact]
    public void Target_controls_must_be_in_scope()
    {
        var target = ScopeFixture.Target() with { ControlRefIds = ["AC-MFA", "AC-NONE"] };

        var problem = ScopeFixture.Problems(targets: target).ShouldHaveSingleItem();

        problem.Code.ShouldBe("config.target_not_in_scope");
        problem.Message.ShouldContain("AC-NONE");
    }

    [Fact]
    public void Two_audits_with_one_ref_id_are_reported()
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource("scope/audits/a.yaml", ScopeFixture.Audit), new ScopeSource("scope/audits/b.yaml", ScopeFixture.Audit)]);

        var problem = ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []).ShouldHaveSingleItem();

        (problem.Code, problem.At.File).ShouldBe(("scope.duplicate", "scope/audits/b.yaml"));
    }

    [Fact]
    public void Framework_without_a_snapshot_is_reported_once_and_not_checked_further()
    {
        var parsed = ScopeFixture.Parse(audit: ScopeFixture.Audit.Replace("groups: [\"1\"]", "groups: [\"7\"]", StringComparison.Ordinal));

        var problem = ScopeValidator.Validate(parsed.Scope, new Dictionary<string, Frameworks.FrameworkSnapshot>(), []).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.framework_snapshot_missing", new SourceLine("scope/setup.yaml", 4)));
    }
}
