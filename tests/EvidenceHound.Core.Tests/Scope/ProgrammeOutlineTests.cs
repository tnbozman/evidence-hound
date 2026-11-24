using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s11-01: a programme's outline counts the snapshot's assessable requirements per group and per parent node.</summary>
public class ProgrammeOutlineTests
{
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    [Fact]
    public void Counts_requirements_per_group_and_parent()
    {
        var snapshot = Snapshot("essential-eight-2023.json");
        var programme = Programme(snapshot.FrameworkUrn, "1", "2");

        var outline = ProgrammeOutline.Of(programme, snapshot);

        outline.Version.ShouldBe(snapshot.Version);
        outline.Groups.Select(g => (g.RefId, g.Name, g.Requirements)).ShouldBe([("1", "Maturity Level One", 48), ("2", "Maturity Level Two", 107)]);
        Counts(outline.Groups[0]).ShouldBe(new Dictionary<string, int> { ["pa"] = 9, ["pos"] = 8, ["mfa"] = 7, ["rap"] = 7, ["ac"] = 3, ["rmom"] = 4, ["uah"] = 4, ["rb"] = 6 });
        Counts(outline.Groups[1]).ShouldBe(new Dictionary<string, int> { ["pa"] = 11, ["pos"] = 8, ["mfa"] = 19, ["rap"] = 20, ["ac"] = 14, ["rmom"] = 5, ["uah"] = 22, ["rb"] = 8 });
        outline.Groups[0].Parents[0].Urn.ShouldBe("urn:intuitem:risk:req_node:essential-eight-2023:pa");
    }

    [Fact]
    public void Requirements_without_a_parent_are_counted_at_the_top_level()
    {
        var snapshot = new FrameworkSnapshot("urn:example:risk:library:x", "urn:example:risk:framework:x", 1, [new ImplementationGroup("1", "One")],
            [new SnapshotNode("urn:example:risk:req_node:x:1", "X-1", null, ["1"], "One"), new SnapshotNode("urn:example:risk:req_node:x:2", "X-2", null, ["1"], "Two")]);

        var parent = ProgrammeOutline.Of(Programme(snapshot.FrameworkUrn, "1"), snapshot).Groups.ShouldHaveSingleItem().Parents.ShouldHaveSingleItem();

        (parent.Urn, parent.Name, parent.Requirements).ShouldBe((null, "(top level)", 2));
    }

    /// <summary>Every assessable node in a selected group is counted exactly once in that group, under its own parent.</summary>
    [Property(MaxTest = 200)]
    public void Every_assessable_node_is_counted_once_per_group(byte[] parents, byte[] masks)
    {
        var count = Math.Min(parents.Length, masks.Length);
        string[] groups = ["1", "2", "3"];
        var nodes = Enumerable.Range(0, count).Select(i => new SnapshotNode(
            $"urn:example:risk:req_node:x:{i}",
            $"X-{i}",
            parents[i] % 4 == 0 ? null : $"urn:example:risk:req_node:x:p{parents[i] % 4}",
            [.. groups.Where((_, g) => (masks[i] & (1 << g)) != 0)],
            $"Node {i}")).ToArray();
        var snapshot = new FrameworkSnapshot("urn:example:risk:library:x", "urn:example:risk:framework:x", 1, [.. groups.Select(g => new ImplementationGroup(g, g))], nodes);

        var outline = ProgrammeOutline.Of(Programme(snapshot.FrameworkUrn, groups), snapshot);

        foreach (var group in outline.Groups)
        {
            var expected = nodes.Where(n => n.Groups.Contains(group.RefId)).ToArray();
            group.Requirements.ShouldBe(expected.Length);
            group.Parents.Sum(p => p.Requirements).ShouldBe(expected.Length);
            group.Parents.Select(p => p.Urn).ShouldBeUnique();
            foreach (var parent in group.Parents)
            {
                parent.Requirements.ShouldBe(expected.Count(n => n.ParentUrn == parent.Urn));
            }
        }
    }

    internal static FrameworkSnapshot Snapshot(string file)
    {
        var read = FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "frameworks", file)), file);
        return read.ShouldBeOfType<Result<FrameworkSnapshot>.Ok>().Value;
    }

    private static ScopeProgramme Programme(string framework, params string[] groups) =>
        new("example", "Example", new ScopeRef(framework, At), groups, new ScopeRef("EX-2026", At), "parent", At);

    private static Dictionary<string, int> Counts(GroupOutline group) => group.Parents.ToDictionary(p => p.Name, p => p.Requirements);
}
