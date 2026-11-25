using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Mapping;

/// <summary>s14-01: <c>hound mapping derive</c> drafts a mapping for a new framework from an old framework's mapping and a transition mapping.</summary>
public class DeriveTests
{
    private const string Old = "urn:example:risk:req_node:example-old:mfa";
    private const string Bnd = "urn:example:risk:req_node:example-domains:bnd";
    private const string Idn = "urn:example:risk:req_node:example-domains:idn";

    private static readonly FrameworkSnapshot Source = new(
        "urn:example:risk:library:example-old",
        "urn:example:risk:framework:example-old",
        1,
        [new ImplementationGroup("1", "Level one")],
        [
            new SnapshotNode($"{Old}:old-1", "OLD-1", Old, ["1"], "Old one"),
            new SnapshotNode($"{Old}:old-2", "OLD-2", Old, ["1"], "Old two"),
            new SnapshotNode($"urn:example:risk:req_node:example-old:rb:old-2", "OLD-2", "urn:example:risk:req_node:example-old:rb", ["1"], "Old two again"),
        ]);

    private static readonly FrameworkSnapshot Target = new(
        "urn:example:risk:library:example-domains",
        "urn:example:risk:framework:example-domains",
        1,
        [new ImplementationGroup("1", "Phase one"), new ImplementationGroup("2", "Phase two")],
        [
            new SnapshotNode($"{Bnd}:pro-01:pro-01.1", "PRO-01.1", $"{Bnd}:pro-01", ["1"], "Action one"),
            new SnapshotNode($"{Idn}:pro-01:pro-01.1", "PRO-01.1", $"{Idn}:pro-01", ["1"], "Action one"),
            new SnapshotNode($"{Idn}:pro-02:pro-02.1", "PRO-02.1", $"{Idn}:pro-02", ["2"], "Action two"),
        ])
    {
        Parents =
        [
            new SnapshotParent(Bnd, "BND", "Boundary services"),
            new SnapshotParent($"{Bnd}:pro-01", "PRO-01", "Strong authentication") { ParentUrn = Bnd },
            new SnapshotParent(Idn, "IDN", "Identity services"),
            new SnapshotParent($"{Idn}:pro-01", "PRO-01", "Strong authentication") { ParentUrn = Idn },
            new SnapshotParent($"{Idn}:pro-02", "PRO-02", "Backups") { ParentUrn = Idn },
        ],
    };

    private static readonly MappingLibrary Links = new(
        Source.FrameworkUrn,
        Target.FrameworkUrn,
        [
            new RequirementLink($"{Old}:old-1", $"{Bnd}:pro-01:pro-01.1", CrosswalkRelationship.Intersect),
            new RequirementLink($"{Old}:old-2", $"{Bnd}:pro-01:pro-01.1", CrosswalkRelationship.Intersect),
            new RequirementLink($"{Old}:old-2", $"{Idn}:pro-02:pro-02.1", CrosswalkRelationship.Intersect),
        ]);

    [Fact]
    public void Action_gets_union_of_source_checks_marked_draft()
    {
        var mapping = Mapping(
            Requirement(RequirementKey.Ref("OLD-1"), "identity.mfa.enforced", "identity.mfa.registered"),
            Requirement(RequirementKey.Ref("OLD-2", allRows: true), "identity.mfa.registered", "backup.restore_tested"));

        var derived = MappingDerivation.Derive(mapping, Source, Target, Links, ["1"]).Value();

        derived.FrameworkUrn.ShouldBe(Target.FrameworkUrn);
        var first = derived.Requirements[0];
        first.Urn.ShouldBe($"{Bnd}:pro-01:pro-01.1");
        first.Domain.ShouldBe("bnd");
        first.SourceRefs.ShouldBe(["OLD-1", "OLD-2"]);
        first.Checks.Select(c => c.Value).ShouldBe(["identity.mfa.enforced", "identity.mfa.registered", "backup.restore_tested"]);
        derived.ToMapping().Value().Requirements.ShouldAllBe(r => r.Draft);
    }

    [Fact]
    public void Every_target_row_in_the_groups_is_listed_and_rows_without_checks_have_none()
    {
        var mapping = Mapping(Requirement(RequirementKey.Ref("OLD-1"), "identity.mfa.enforced"));

        var derived = MappingDerivation.Derive(mapping, Source, Target, Links, ["1"]).Value();

        derived.Requirements.Select(r => (r.Domain, r.RefId, r.Checks.Count)).ShouldBe([("bnd", "PRO-01.1", 1), ("idn", "PRO-01.1", 0)]);
        derived.ToMapping().Value().Requirements.Count.ShouldBe(1);
    }

    [Fact]
    public void Links_must_join_the_two_frameworks()
    {
        var wrong = Links with { SourceFrameworkUrn = "urn:example:risk:framework:other" };

        var derived = MappingDerivation.Derive(Mapping(Requirement(RequirementKey.Ref("OLD-1"), "identity.mfa.enforced")), Source, Target, wrong, ["1"]);

        derived.Match(_ => [], ps => ps.Select(p => p.Code)).ShouldBe(["derive.frameworks_mismatch"]);
    }

    private static RequirementMapping Requirement(RequirementKey key, params string[] checks) =>
        RequirementMapping.Create(key, checks.Select(c => CheckId.Create(c).Value()), MappingRule.AllPass).Value();

    private static FrameworkMapping Mapping(params RequirementMapping[] requirements) =>
        FrameworkMapping.Create(Source.FrameworkUrn, requirements).Value();
}
