using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli.Tests;

/// <summary>s09-36: the plan lists each requirement left open because another system meets it, with what it waits on.</summary>
public sealed class OpenDependencyPlanTests
{
    private static readonly ScopePlan Empty = new([], []);

    private static readonly PlanNotes Notes = PlanNotes.None with
    {
        Dependencies = [new OpenDependency("EX-E8-2026", "ISM-1501", "Example laptops", null, "no audit yet"), new OpenDependency("EX-E8-2026", "ISM-1173", "Example 365", "EX365-E8", "compliant")],
    };

    [Fact]
    public void Markdown_lists_each_open_dependency()
    {
        var markdown = ScopePlanOutput.Markdown(Empty, Notes);

        markdown.ShouldContain("### Open dependencies");
        markdown.ShouldContain("| EX-E8-2026 | ISM-1501 | Example laptops | (none) | no audit yet |");
        markdown.ShouldContain("| EX-E8-2026 | ISM-1173 | Example 365 | EX365-E8 | compliant |");
    }

    [Fact]
    public void Json_lists_each_open_dependency()
    {
        var dependency = JsonNode.Parse(ScopePlanOutput.Json(Empty, Notes))!["dependencies"]!.AsArray()[1]!;

        (dependency["audit"]!.GetValue<string>(), dependency["requirement"]!.GetValue<string>(), dependency["system"]!.GetValue<string>()).ShouldBe(("EX-E8-2026", "ISM-1173", "Example 365"));
        (dependency["system_audit"]!.GetValue<string>(), dependency["result_there"]!.GetValue<string>()).ShouldBe(("EX365-E8", "compliant"));
    }

    [Fact]
    public void Without_open_dependencies_there_is_no_section()
    {
        ScopePlanOutput.Markdown(Empty, PlanNotes.None).ShouldNotContain("Open dependencies");
    }
}
