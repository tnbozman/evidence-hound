using EvidenceHound.Core.Mapping;

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
}
