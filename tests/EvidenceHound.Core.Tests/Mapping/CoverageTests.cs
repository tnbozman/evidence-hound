using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Mapping;

/// <summary>s12-01: <c>hound mapping coverage</c> counts how each requirement is answered, per parent node and implementation group.</summary>
public class CoverageTests
{
    private const string Mfa = "urn:example:node:mfa";
    private const string Backups = "urn:example:node:rb";

    private static readonly string[] Ml2 = ["1", "2"];

    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:library",
        "urn:example:framework",
        1,
        [new ImplementationGroup("1", "Maturity Level One"), new ImplementationGroup("2", "Maturity Level Two"), new ImplementationGroup("3", "Maturity Level Three")],
        [
            Node("MFA-A", Mfa, "1", "2"),
            Node("MFA-B", Mfa, "2"),
            Node("RB-A", Backups, "1", "2"),
            Node("RB-B", Backups, "1"),
            Node("RB-C", Backups, "3"),
        ]);

    private static readonly IReadOnlySet<CheckId> ManualChecks = new HashSet<CheckId> { Id("backup.restore_tested"), Id("identity.mfa.reviewed") };

    [Fact]
    public void Counts_per_strategy_and_maturity_level()
    {
        var mapping = Mapping(
            Requirement("MFA-A", "identity.mfa.enforced"),
            Requirement("MFA-B", "identity.mfa.reviewed"));

        var counts = MappingCoverage.Count(Snapshot, mapping, [RequirementKey.Ref("RB-A")], ManualChecks, Ml2);

        counts.ShouldBe(
        [
            new CoverageCount(Mfa, "1", Automated: 1, Manual: 0, Uncovered: 0),
            new CoverageCount(Mfa, "2", Automated: 1, Manual: 1, Uncovered: 0),
            new CoverageCount(Backups, "1", Automated: 0, Manual: 1, Uncovered: 1),
            new CoverageCount(Backups, "2", Automated: 0, Manual: 1, Uncovered: 0),
        ]);
    }

    [Fact]
    public void A_requirement_with_any_collector_check_is_automated()
    {
        var mapping = Mapping(Requirement("RB-B", "backup.restore_tested", "backup.job_succeeded"));

        var counts = MappingCoverage.Count(Snapshot, mapping, [], ManualChecks, ["1"]);

        counts.Single(c => c.ParentUrn == Backups).ShouldBe(new CoverageCount(Backups, "1", Automated: 1, Manual: 0, Uncovered: 1));
    }

    [Fact]
    public void Groups_outside_the_audit_selection_are_not_counted()
    {
        var counts = MappingCoverage.Count(Snapshot, Mapping(Requirement("MFA-A", "identity.mfa.enforced")), [], ManualChecks, ["3"]);

        counts.ShouldBe([new CoverageCount(Backups, "3", Automated: 0, Manual: 0, Uncovered: 1)]);
    }

    private static SnapshotNode Node(string refId, string parent, params string[] groups) =>
        new($"{parent}:{refId.ToLowerInvariant()}", refId, parent, groups, refId);

    private static CheckId Id(string name) => CheckId.Create(name).Value();

    private static RequirementMapping Requirement(string refId, params string[] checks) =>
        RequirementMapping.Create(refId, checks.Select(Id), MappingRule.AllPass).Value();

    private static FrameworkMapping Mapping(params RequirementMapping[] requirements) =>
        FrameworkMapping.Create("urn:example:framework", requirements).Value();
}
