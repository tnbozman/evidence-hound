using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s14-01: a snapshot keeps every named ancestor of a requirement, so coverage can count per ASD Essentials domain.</summary>
public class DomainSnapshotTests
{
    private static readonly string Bnd = "urn:example:risk:req_node:example-domains:bnd";

    [Fact]
    public void Parents_include_ancestors_with_their_own_parent()
    {
        var snapshot = FrameworkLibraryReader.Snapshot(RepoFiles.DomainsLibrary, "example-domains-library.yaml").Value();

        snapshot.Parents.Select(p => (p.RefId, p.ParentUrn)).ShouldBe([
            ("BND", (string?)null),
            ("PRO-01", Bnd),
            ("IDN", null),
            ("PRO-01", "urn:example:risk:req_node:example-domains:idn"),
        ]);
        snapshot.TopUrn(snapshot.Nodes[0].ParentUrn!).ShouldBe(Bnd);
        snapshot.TopUrn(Bnd).ShouldBe(Bnd);

        var read = FrameworkSnapshotJson.Read(FrameworkSnapshotJson.Write(snapshot), "snapshot.json").Value();
        read.Parents.ShouldBe(snapshot.Parents);
    }
}
