using EvidenceHound.Core.Frameworks;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s14-02: DISP membership readiness as a custom CISO Assistant library, written from the public Defence Security Principles Framework
/// control areas. Requirement text is paraphrased in Evidence Hound's own words, and each requirement cites its source area.
/// </summary>
public class DispLibraryTests
{
    private static readonly string[] Areas = ["DISP-GOV", "DISP-PER", "DISP-PHY", "DISP-INF", "DISP-CYB"];

    private static string Library => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "disp.yaml"));

    private static List<YamlMappingNode> Nodes()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(Library));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return [.. ((YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)root["objects"])["framework"])["requirement_nodes"]).Children.Cast<YamlMappingNode>()];
    }

    [Fact]
    public void Every_requirement_has_a_source_area_and_no_copied_text()
    {
        var nodes = Nodes();

        nodes.Where(n => Field(n, "depth") == "1").Select(n => Field(n, "ref_id")).ShouldBe(Areas);
        var requirements = nodes.Where(n => Field(n, "assessable") == "true").ToList();
        requirements.Count.ShouldBeGreaterThanOrEqualTo(20);
        foreach (var requirement in requirements)
        {
            var refId = Field(requirement, "ref_id")!;
            Areas.ShouldContain(refId[..8], refId);
            Field(requirement, "annotation")!.ShouldStartWith("Source: Defence Security Principles Framework", customMessage: refId);
            var description = Field(requirement, "description")!;
            description.Length.ShouldBeLessThanOrEqualTo(300, refId);
            description.ShouldNotContain("\"", customMessage: refId);
            description.ShouldNotContain("must", Case.Insensitive, refId);
        }
    }

    [Fact]
    public void The_committed_snapshot_matches_the_library()
    {
        var snapshot = FrameworkLibraryReader.Snapshot(Library, "disp.yaml").Value();
        var committed = FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "disp.json")), "disp.json").Value();

        FrameworkSnapshotJson.Write(committed).ShouldBe(FrameworkSnapshotJson.Write(snapshot));
        snapshot.FrameworkUrn.ShouldBe("urn:evidence-hound:risk:framework:disp-membership");
        snapshot.Groups.Select(g => g.RefId).ShouldBe(["M"]);
        snapshot.Nodes.ShouldAllBe(n => n.Groups.Contains("M"));
    }

    private static string? Field(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}
