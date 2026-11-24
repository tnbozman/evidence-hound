using System.Text.RegularExpressions;
using EvidenceHound.Core.Frameworks;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s11-09: AI6 as a CISO Assistant library: six practices, 21 assessable sub-practices, groups F (Foundations) and I (Implementation), and a
/// committed snapshot that matches it. The Foundations group follows the trace table in docs/ai-grc.md.
/// </summary>
public partial class Ai6LibraryTests
{
    private static readonly string[] SubPractices =
    [
        "AI6-1.1", "AI6-1.2", "AI6-1.3", "AI6-1.4", "AI6-2.1", "AI6-2.2", "AI6-2.3", "AI6-3.1", "AI6-3.2", "AI6-3.3", "AI6-3.4",
        "AI6-4.1", "AI6-4.2", "AI6-4.3", "AI6-4.4", "AI6-5.1", "AI6-5.2", "AI6-5.3", "AI6-5.4", "AI6-6.1", "AI6-6.2",
    ];

    private static string Library => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "ai6.yaml"));

    private static FrameworkSnapshot Snapshot => FrameworkLibraryReader.Snapshot(Library, "ai6.yaml").Value();

    [Fact]
    public void Has_six_practices_and_twenty_one_assessable_sub_practices()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(Library));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        ((YamlScalarNode)root["urn"]).Value.ShouldBe("urn:evidence-hound:risk:library:ai6");
        var nodes = ((YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)root["objects"])["framework"])["requirement_nodes"]).Children.Cast<YamlMappingNode>().ToList();

        nodes.Where(n => Field(n, "depth") == "1").Select(n => (Field(n, "ref_id"), Field(n, "assessable")))
            .ShouldBe([("AI6-1", "false"), ("AI6-2", "false"), ("AI6-3", "false"), ("AI6-4", "false"), ("AI6-5", "false"), ("AI6-6", "false")]);
        nodes.Where(n => Field(n, "depth") == "2").Select(n => Field(n, "ref_id")).ShouldBe(SubPractices);
        nodes.Where(n => Field(n, "depth") == "2").Select(n => Field(n, "assessable")).ShouldAllBe(a => a == "true");
        Snapshot.FrameworkUrn.ShouldBe("urn:evidence-hound:risk:framework:ai6");
        Snapshot.Nodes.Select(n => n.RefId).ShouldBe(SubPractices);
        Snapshot.Nodes.ShouldAllBe(n => n.Description.Contains("ai.gov.au", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_sub_practice_is_in_implementation_group()
    {
        Snapshot.Groups.Select(g => g.RefId).ShouldBe(["F", "I"]);
        Snapshot.Nodes.ShouldAllBe(n => n.Groups.Contains("I"));
    }

    [Fact]
    public void Foundations_group_matches_trace_table()
    {
        var doc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "docs", "ai-grc.md"));
        var rows = TraceRow().Matches(doc).Select(m => (Action: m.Groups["action"].Value, SubPractice: m.Groups["sub"].Value)).ToList();

        rows.Count.ShouldBe(30, doc);
        rows.Select(r => r.Action).Distinct().Count().ShouldBe(30);
        rows.Select(r => r.SubPractice).ShouldAllBe(s => SubPractices.Contains(s));
        Snapshot.Nodes.Where(n => n.Groups.Contains("F")).Select(n => n.RefId!)
            .ShouldBe(SubPractices.Where(s => rows.Any(r => r.SubPractice == s)));
    }

    [Fact]
    public void Snapshot_matches_library()
    {
        var committed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "ai6.json"));

        FrameworkSnapshotJson.Write(Snapshot).ShouldBe(committed);
    }

    private static string Field(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value!;

    [GeneratedRegex(@"^\| F(?<action>[1-6]\.[0-9]) \|[^|\n]+\|[^|\n]+\| (?<sub>AI6-[1-6]\.[1-4]) \|", RegexOptions.Multiline)]
    private static partial Regex TraceRow();
}
