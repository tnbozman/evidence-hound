using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli.Tests;

/// <summary>s09-28: a mitigation no product provides is listed by the plan as a gap, never as a fail on some system.</summary>
public sealed class CapabilityGapPlanTests
{
    private static readonly ScopePlan Empty = new([], []);

    private static readonly PlanNotes Notes = PlanNotes.None with { Gaps = [new CapabilityGap("pa", ["ISM-1690", "ISM-1691"])] };

    [Fact]
    public void Markdown_lists_each_gap_with_the_requirements_no_audit_answers()
    {
        var markdown = ScopePlanOutput.Markdown(Empty, Notes);

        markdown.ShouldContain("### Capability gaps");
        markdown.ShouldContain("| pa | 2 | ISM-1690, ISM-1691 |");
    }

    [Fact]
    public void Json_lists_each_gap()
    {
        var gap = JsonNode.Parse(ScopePlanOutput.Json(Empty, Notes))!["gaps"]!.AsArray().ShouldHaveSingleItem()!;

        gap["mitigation"]!.GetValue<string>().ShouldBe("pa");
        gap["requirements"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["ISM-1690", "ISM-1691"]);
    }

    [Fact]
    public void Without_gaps_there_is_no_section()
    {
        ScopePlanOutput.Markdown(Empty, PlanNotes.None).ShouldNotContain("Capability gaps");
    }
}
