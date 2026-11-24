using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-05: <c>hound ssp diff</c> lists only what changed between two versions, boundary and inventory first.</summary>
public class SspDiffTests
{
    [Fact]
    public void Boundary_change_is_listed_first()
    {
        var before = Snapshot(SspLayoutTests.Example());
        var example = SspLayoutTests.Example();
        var source = example.Perimeter.Source;
        var changed = example with
        {
            Version = new SspValue("0.2", source),
            Perimeter = new SspValue("Corporate IT and the warehouse", source),
            Inventory = [.. example.Inventory, new SspSystem(new SspValue("Warehouse scanners", source), new SspValue("supporting", source), new SspValue("it@example.com", source), new SspValue(null, source))],
            Requirements = [.. example.Requirements.Select(r => r.Ref.Value == "ISM-1504" ? r with { Status = r.Status with { Value = SspStatements.Implemented } } : r)],
        };

        var changes = SspDiff.Of(before, Snapshot(changed));

        changes.Select(c => (c.Area, c.Item)).ShouldBe(
        [
            ("boundary", "perimeter"),
            ("inventory", "Warehouse scanners"),
            ("status", "ISM-1504"),
        ]);
        changes[0].Before.ShouldBe("Corporate IT");
        changes[0].After.ShouldBe("Corporate IT and the warehouse");
        SspDiff.Markdown("0.1", "0.2", changes).ShouldContain("The boundary or inventory changed: this usually needs a new major version.");
    }

    [Fact]
    public void Same_plan_has_no_changes()
    {
        SspDiff.Of(Snapshot(SspLayoutTests.Example()), Snapshot(SspLayoutTests.Example())).ShouldBeEmpty();
        SspDiff.Markdown("0.1", "0.1", []).ShouldContain("No changes.");
    }

    [Fact]
    public void Statements_suppliers_and_sections_are_compared()
    {
        var example = SspLayoutTests.Example();
        var source = example.Perimeter.Source;
        var changed = example with
        {
            ExternalServices = [],
            Requirements = [.. example.Requirements.Select(r => r.Ref.Value == "ISM-1173" ? r with { Statement = r.Statement with { Value = "A new statement." } } : r)],
        };
        var texts = new Dictionary<string, string>(SspLayoutTests.Texts()) { ["purpose"] = "A new purpose." };

        var changes = SspDiff.Of(Snapshot(example), new SspSnapshot(changed, texts));

        changes.Select(c => (c.Area, c.Item)).ShouldBe([("supplier", "Example 365"), ("statement", "ISM-1173"), ("section", "purpose")]);
        changes[0].After.ShouldBeNull();
    }

    private static SspSnapshot Snapshot(SspDocument document) => new(document, SspLayoutTests.Texts());
}
