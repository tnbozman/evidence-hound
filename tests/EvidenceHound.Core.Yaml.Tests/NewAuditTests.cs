using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

public class NewAuditTests
{
    [Fact]
    public void Lists_every_in_group_requirement_with_title()
    {
        var yaml = AuditTemplate.Write(ScopeFixture.Snapshot, ["1"], "EX-NEW-2026");

        yaml.ShouldContain("ref_id: EX-NEW-2026");
        yaml.ShouldContain($"framework: {ScopeFixture.FrameworkUrn}");
        yaml.ShouldContain("  # Every account uses multi-factor authentication\n  - ref: EX-1.1\n");
        yaml.ShouldContain("  # Phishing-resistant MFA for admins\n  - ref: EX-1.2\n");
        yaml.ShouldNotContain("EX-2.1");
    }

    [Fact]
    public void Written_file_parses_with_every_requirement_to_do_and_not_assessed()
    {
        var yaml = AuditTemplate.Write(ScopeFixture.Snapshot, ["1", "2"], "EX-NEW-2026");

        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup), new ScopeSource("scope/controls.yaml", ScopeFixture.Controls), [new ScopeSource("scope/audits/new.yaml", yaml)]);

        parsed.Problems.ShouldBeEmpty();
        var audit = parsed.Scope.Audits.ShouldHaveSingleItem();
        audit.Groups.Select(g => g.Value).ShouldBe(["1", "2"]);
        audit.Assessments.Select(a => (a.Requirement.Key.Value, a.Status, a.Result)).ShouldBe(
            [("EX-1.1", "to_do", ComplianceResult.NotAssessed), ("EX-1.2", "to_do", ComplianceResult.NotAssessed), ("EX-2.1", "to_do", ComplianceResult.NotAssessed)]);
        audit.Unlisted.ShouldNotBeNull().Result.ShouldBe(ComplianceResult.NotAssessed);
    }

    [Fact]
    public void A_ref_repeated_in_the_framework_is_written_by_urn()
    {
        var snapshot = ScopeFixture.Snapshot with
        {
            Nodes = [.. ScopeFixture.Snapshot.Nodes, new Frameworks.SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1-b", "EX-1.1", null, ["1"], "Again for another system")],
        };

        var yaml = AuditTemplate.Write(snapshot, ["1"], "EX-NEW-2026");

        yaml.ShouldContain("  - urn: urn:example:risk:req_node:example-baseline:ex-1.1\n");
        yaml.ShouldContain("  - urn: urn:example:risk:req_node:example-baseline:ex-1.1-b\n");
    }
}
