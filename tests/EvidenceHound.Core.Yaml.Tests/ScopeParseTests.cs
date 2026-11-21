using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

public class ScopeParseTests
{
    [Fact]
    public void Reads_setup_and_audit_files()
    {
        var parsed = ScopeFixture.Parse();

        parsed.Problems.ShouldBeEmpty();
        var scope = parsed.Scope;
        scope.Setup.RootFolder.ShouldBe("Global");
        scope.Setup.Frameworks.ShouldHaveSingleItem().ShouldBe(new ScopeFramework(
            ScopeFixture.FrameworkUrn, "urn:example:risk:library:example-baseline", null, "../frameworks/example-baseline.json", new SourceLine("scope/setup.yaml", 4)));
        var domain = scope.Setup.Domains.ShouldHaveSingleItem();
        domain.Name.ShouldBe("Example Pty Ltd");
        domain.Perimeters.ShouldHaveSingleItem().ShouldBe(new ScopePerimeter("Example baseline", "PER-BASE", null, new SourceLine("scope/setup.yaml", 11)));
        domain.Systems.Select(s => (s.Name, s.Type)).ShouldBe([("Example 365", SystemType.Primary), ("Example laptops", SystemType.Supporting)]);
        domain.Systems[1].Parents.ShouldHaveSingleItem().Value.ShouldBe("Example 365");
        domain.Systems[1].Tags.ShouldBe(new Dictionary<string, string> { ["cui"] = "true" });

        scope.Controls.Select(c => c.RefId).ShouldBe(["AC-MFA", "AC-BACKUP"]);
        scope.Controls[0].Documents.ShouldHaveSingleItem().Value.ShouldBe("mfa-policy");

        var audit = scope.Audits.ShouldHaveSingleItem();
        (audit.RefId, audit.Framework.Value, audit.Domain.Value, audit.Perimeter.Value).ShouldBe(("EXAMPLE-BASELINE-2026", ScopeFixture.FrameworkUrn, "Example Pty Ltd", "Example baseline"));
        audit.Groups.Select(g => g.Value).ShouldBe(["1"]);
        (audit.Eta, audit.DueDate).ShouldBe(("2026-06-30", "2026-07-31"));
        audit.FieldVisibility["observation"].Auditor.ShouldBe("edit");
        audit.Documents.ShouldHaveSingleItem().Requirements.ShouldHaveSingleItem().Key.ShouldBe(RequirementKey.Ref("EX-1.1"));
        audit.Controls.ShouldHaveSingleItem().Requirements.Select(r => r.Key.Value).ShouldBe(["EX-1.1", "EX-1.2"]);
        var assessment = audit.Assessments.ShouldHaveSingleItem();
        (assessment.Requirement.Key, assessment.Status, assessment.Result, assessment.Observation)
            .ShouldBe((RequirementKey.Ref("EX-1.2"), "done", ComplianceResult.Compliant, "Reviewed by hand."));
        audit.Unlisted.ShouldNotBeNull().Result.ShouldBe(ComplianceResult.NotAssessed);
    }

    [Fact]
    public void Single_perimeter_is_read_as_one_item_list()
    {
        var setup = ScopeFixture.Setup.Replace("    perimeters:\n      - name: Example baseline\n        ref_id: PER-BASE", "    perimeter:\n      name: Example baseline\n      ref_id: PER-BASE", StringComparison.Ordinal);

        var parsed = ScopeFixture.Parse(setup: setup);

        parsed.Problems.ShouldBeEmpty();
        parsed.Scope.Setup.Domains.ShouldHaveSingleItem().Perimeters.ShouldHaveSingleItem().Name.ShouldBe("Example baseline");
    }

    [Fact]
    public void Requirement_given_by_urn_is_read_as_a_urn_key()
    {
        var audit = ScopeFixture.Audit.Replace("requirements: [EX-1.1]\n", "requirements: [\"urn:example:risk:req_node:example-baseline:ex-1.1\"]\n", StringComparison.Ordinal);

        var parsed = ScopeFixture.Parse(audit: audit);

        parsed.Scope.Audits[0].Documents[0].Requirements[0].Key.ShouldBe(RequirementKey.Urn("urn:example:risk:req_node:example-baseline:ex-1.1"));
    }

    [Fact]
    public void Unknown_keys_are_reported_with_file_and_line()
    {
        var audit = ScopeFixture.Audit.Replace("eta: \"2026-06-30\"\n", "eta: \"2026-06-30\"\ncolour: blue\n", StringComparison.Ordinal);

        var problem = ScopeFixture.Parse(audit: audit).Problems.ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.key_unknown", new SourceLine(ScopeFixture.AuditPath, 8)));
        problem.Message.ShouldContain("colour");
    }

    [Fact]
    public void Duplicate_keys_are_reported_with_file_and_line()
    {
        var audit = ScopeFixture.Audit.Replace("eta: \"2026-06-30\"\n", "eta: \"2026-06-30\"\neta: \"2026-09-30\"\n", StringComparison.Ordinal);

        var problem = ScopeFixture.Parse(audit: audit).Problems.ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("scope.key_duplicate", new SourceLine(ScopeFixture.AuditPath, 8)));
    }

    [Fact]
    public void Problems_in_several_files_are_all_reported()
    {
        var setup = ScopeFixture.Setup.Replace("        type: supporting\n", "        type: secondary\n", StringComparison.Ordinal);
        var audit = ScopeFixture.Audit.Replace("    result: compliant\n", "    result: mostly\n", StringComparison.Ordinal);

        var problems = ScopeFixture.Parse(setup: setup, audit: audit).Problems;

        problems.Select(p => (p.Code, p.At.File)).ShouldBe([("scope.value_invalid", "scope/setup.yaml"), ("scope.value_invalid", ScopeFixture.AuditPath)], ignoreOrder: true);
    }
}
