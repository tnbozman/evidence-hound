using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Mapping;

/// <summary>s12-08: the NIST SP 800-171 crosswalk from Essential Eight evidence.</summary>
public class CrosswalkTests
{
    private static readonly CrosswalkDefinition File = new(
        "urn:example:framework:e8",
        "urn:example:framework:171",
        [new("3.5.3", "Use multifactor authentication"), new("3.8.9", "Protect backups of CUI"), new("3.1.1", "Limit system access"), new("3.10.1", "Limit physical access")],
        [
            new("3.5.3", "ISM-1504", CrosswalkRelationship.Superset),
            new("3.8.9", "ISM-1511", CrosswalkRelationship.Intersect),
            new("3.1.1", "ISM-1883", CrosswalkRelationship.Equal),
        ],
        "mappings/crosswalks/example.yaml");

    private static readonly Dictionary<string, SourceAnswer> Answers = new(StringComparer.Ordinal)
    {
        ["ISM-1504"] = new("ISM-1504", ["identity.mfa.enforced_conditional_access"], [], ComplianceResult.Compliant),
        ["ISM-1511"] = new("ISM-1511", ["backup.performed_and_retained"], ["backup.performed_and_retained"], ComplianceResult.NonCompliant),
    };

    [Fact]
    public void Every_target_requirement_is_answered_partly_answered_or_open()
    {
        var rows = Crosswalks.Build(File, Answers);

        rows.Select(r => (r.Target.RefId, r.Status)).ShouldBe(
        [
            ("3.5.3", CrosswalkStatus.Answered),
            ("3.8.9", CrosswalkStatus.PartlyAnswered),
            ("3.1.1", CrosswalkStatus.PartlyAnswered), // linked, but ISM-1883 has no checks in the mapping
            ("3.10.1", CrosswalkStatus.Open),
        ]);
        rows[1].Sources.Single().Answer.Result.ShouldBe(ComplianceResult.NonCompliant);
        rows[1].Sources.Single().Answer.ManualChecks.ShouldBe(["backup.performed_and_retained"]);
        rows[2].Sources.Single().Answer.Checks.ShouldBeEmpty();
    }

    [Fact]
    public void Uses_a_loaded_mapping_library_before_the_file()
    {
        var library = File with { Origin = "CISO Assistant mapping library" };

        Crosswalks.Choose(library, File).ShouldBe(library);
        Crosswalks.Choose(null, File).ShouldBe(File);
        Crosswalks.Choose(null, null).ShouldBeNull();
    }

    [Fact]
    public void A_target_listed_by_a_link_but_not_in_the_targets_is_still_reported()
    {
        var crosswalk = File with { Targets = [], Links = [new("3.5.3", "ISM-1504", CrosswalkRelationship.Equal)] };

        Crosswalks.Build(crosswalk, Answers).Single().Target.ShouldBe(new CrosswalkTarget("3.5.3", ""));
    }

    [Fact]
    public void Answers_take_each_source_checks_from_the_mapping_and_the_worst_result_of_its_rows()
    {
        var mapping = FrameworkMapping.Create(
            "urn:example:framework:e8",
            [
                RequirementMapping.Create(RequirementKey.Ref("ISM-1504", allRows: true), [Id("identity.mfa.users")], MappingRule.AllPass).Value(),
                RequirementMapping.Create(RequirementKey.Ref("ISM-1511"), [Id("backup.performed"), Id("backup.restore_tested")], MappingRule.AllPass).Value(),
            ]).Value();
        CurrentRow[] rows =
        [
            Row("ra-1", "urn:e8:mfa:ism-1504", "ISM-1504", ComplianceResult.Compliant),
            Row("ra-2", "urn:e8:apps:ism-1504", "ISM-1504", ComplianceResult.PartiallyCompliant),
            Row("ra-3", "urn:e8:rb:ism-1511", "ISM-1511", ComplianceResult.NotAssessed),
            Row("ra-4", "urn:e8:rb:ism-1515", "ISM-1515", ComplianceResult.NonCompliant),
        ];

        var answers = Crosswalks.Answers(mapping, Snapshot(), new HashSet<CheckId> { Id("backup.restore_tested") }, rows);

        answers["ISM-1504"].ShouldBe(new SourceAnswer("ISM-1504", ["identity.mfa.users"], [], ComplianceResult.PartiallyCompliant), new AnswerComparer());
        answers["ISM-1511"].Checks.ShouldBe(["backup.performed", "backup.restore_tested"]);
        answers["ISM-1511"].ManualChecks.ShouldBe(["backup.restore_tested"]);
        answers["ISM-1511"].Result.ShouldBe(ComplianceResult.NotAssessed);
        answers["ISM-1515"].Checks.ShouldBeEmpty(); // in the audit, not in the mapping: a person records it
        answers["ISM-1515"].Result.ShouldBe(ComplianceResult.NonCompliant);
    }

    [Fact]
    public void Answers_name_requirements_mapped_by_urn_by_their_ref_and_without_an_audit_have_no_result()
    {
        var mapping = FrameworkMapping.Create(
            "urn:example:framework:e8",
            [RequirementMapping.Create(RequirementKey.Urn("urn:e8:mfa:ism-1504"), [Id("identity.mfa.users")], MappingRule.AllPass).Value()]).Value();

        var answers = Crosswalks.Answers(mapping, Snapshot(), new HashSet<CheckId>(), []);

        answers["ISM-1504"].Checks.ShouldBe(["identity.mfa.users"]);
        answers["ISM-1504"].Result.ShouldBeNull();
    }

    private static CheckId Id(string id) => CheckId.Create(id).Value();

    private static CurrentRow Row(string id, string urn, string refId, ComplianceResult result) => new(id, urn, refId, ["1"], result, "", []);

    private static FrameworkSnapshot Snapshot() => new(
        "urn:example:library:e8",
        "urn:example:framework:e8",
        1,
        [new("1", "ML1")],
        [new("urn:e8:mfa:ism-1504", "ISM-1504", "urn:e8:mfa", ["1"], "MFA"), new("urn:e8:apps:ism-1504", "ISM-1504", "urn:e8:apps", ["1"], "MFA")]);

    private sealed class AnswerComparer : IEqualityComparer<SourceAnswer>
    {
        public bool Equals(SourceAnswer? x, SourceAnswer? y) =>
            x is not null && y is not null && x.SourceRef == y.SourceRef && x.Checks.SequenceEqual(y.Checks) && x.ManualChecks.SequenceEqual(y.ManualChecks) && x.Result == y.Result;

        public int GetHashCode(SourceAnswer obj) => obj.SourceRef.GetHashCode(StringComparison.Ordinal);
    }
}
