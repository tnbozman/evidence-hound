using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml.Tests;

public class FrameworkSnapshotTests
{
    private static readonly string Source = "example-repeated-library.yaml";

    [Fact]
    public void Snapshot_lists_assessable_nodes_with_groups()
    {
        var snapshot = FrameworkLibraryReader.Snapshot(RepoFiles.RepeatedLibrary, Source).Value();

        snapshot.LibraryUrn.ShouldBe("urn:example:risk:library:example-repeated");
        snapshot.FrameworkUrn.ShouldBe("urn:example:risk:framework:example-repeated");
        snapshot.Version.ShouldBe(2);
        snapshot.Groups.Select(g => (g.RefId, g.Name)).ShouldBe([("1", "Phase one"), ("2", "Phase two"), ("3", "Phase three")]);
        snapshot.Nodes.Select(n => (n.RefId, n.Urn)).ShouldBe([
            ("ACT-01", "urn:example:risk:req_node:example-repeated:bnd:act-01"),
            ("ACT-02", "urn:example:risk:req_node:example-repeated:bnd:act-02"),
            ("ACT-01", "urn:example:risk:req_node:example-repeated:idn:act-01"),
            ("ACT-03", "urn:example:risk:req_node:example-repeated:idn:act-03"),
        ]);
        var first = snapshot.Nodes[0];
        first.ParentUrn.ShouldBe("urn:example:risk:req_node:example-repeated:bnd");
        first.Groups.ShouldBe(["1"]);
        first.Title.Length.ShouldBeLessThanOrEqualTo(FrameworkSnapshot.TitleLength);
        first.Title.ShouldStartWith("Multi-factor authentication is enforced");
        snapshot.Nodes[1].Title.ShouldBe("Backups of boundary services are restored and tested.");
    }

    [Fact]
    public void Snapshot_round_trips_through_json()
    {
        var snapshot = FrameworkLibraryReader.Snapshot(RepoFiles.RepeatedLibrary, Source).Value();

        var json = FrameworkSnapshotJson.Write(snapshot);
        var read = FrameworkSnapshotJson.Read(json, "snapshot.json").Value();

        read.FrameworkUrn.ShouldBe(snapshot.FrameworkUrn);
        read.Groups.ShouldBe(snapshot.Groups);
        read.Nodes.Count.ShouldBe(snapshot.Nodes.Count);
        read.Nodes.Select(n => (n.Urn, n.RefId, n.ParentUrn, string.Join(",", n.Groups), n.Title))
            .ShouldBe(snapshot.Nodes.Select(n => (n.Urn, n.RefId, n.ParentUrn, string.Join(",", n.Groups), n.Title)));
        FrameworkSnapshotJson.Write(read).ShouldBe(json);
    }

    [Fact]
    public void A_library_without_a_framework_is_a_problem()
    {
        const string yaml = """
            urn: urn:example:risk:library:example-empty
            version: 1
            objects:
              reference_controls: []
            """;

        FrameworkLibraryReader.Snapshot(yaml, Source).Problems().Select(p => p.Code).ShouldBe(["framework.library_has_no_framework"]);
    }

    [Fact]
    public void Resolve_finds_every_row_with_a_ref_and_the_one_row_with_a_urn()
    {
        var snapshot = FrameworkLibraryReader.Snapshot(RepoFiles.RepeatedLibrary, Source).Value();

        snapshot.Resolve(Mapping.RequirementKey.Ref("ACT-01")).Select(n => n.Urn).ShouldBe([
            "urn:example:risk:req_node:example-repeated:bnd:act-01",
            "urn:example:risk:req_node:example-repeated:idn:act-01",
        ]);
        snapshot.Resolve(Mapping.RequirementKey.Urn("urn:example:risk:req_node:example-repeated:idn:act-03")).ShouldHaveSingleItem().RefId.ShouldBe("ACT-03");
        snapshot.Resolve(Mapping.RequirementKey.Ref("ACT-99")).ShouldBeEmpty();
    }
}
