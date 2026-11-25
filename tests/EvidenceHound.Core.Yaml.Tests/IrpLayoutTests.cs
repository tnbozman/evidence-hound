using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-09: the plan follows NIST SP 800-61 Rev 3, with the Australian reporting inside it, and covers ISM-0043's eight items.</summary>
public class IrpLayoutTests
{
    [Fact]
    public void Covers_every_ism_0043_item()
    {
        var plan = IrpLayout.Markdown(IrpFixture.Build(), IrpFixture.Texts);

        IrpLayout.IsmItems.Count.ShouldBe(8);
        foreach (var (item, i) in IrpLayout.IsmItems.Select((item, i) => (item, i + 1)))
        {
            plan.ShouldContain($"(ISM-0043 item {i})", customMessage: item);
        }

        plan.ShouldContain("Isolate the laptop first.");
        plan.ShouldContain("ISM-0047");
    }

    [Fact]
    public void Sections_follow_sp_800_61r3_with_australian_reporting()
    {
        var plan = IrpLayout.Markdown(IrpFixture.Build(), IrpFixture.Texts);

        var headings = plan.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        string[] functions = ["Govern", "Identify", "Protect", "Detect", "Respond", "Recover"];
        functions.Select(f => Array.FindIndex(headings, h => h.Contains(f, StringComparison.Ordinal))).ShouldBeInOrder(SortDirection.Ascending);
        functions.ShouldAllBe(f => headings.Any(h => h.Contains(f, StringComparison.Ordinal)));

        var respond = plan[plan.IndexOf("Respond", StringComparison.Ordinal)..plan.IndexOf("Recover", StringComparison.Ordinal)];
        respond.ShouldContain("| DFARS 252.204-7012 cyber incident report | DoD, through the DIBNet portal | 72h |");
        respond.ShouldContain("ReportCyber");
        plan.ShouldContain("NIST SP 800-61 Rev 3");
        plan.ShouldContain("NIST SP 800-171 3.6.1");
        plan.ShouldContain("ACSC Cyber Incident Response Plan Guidance");
        plan.ShouldContain("Version 0.1");
    }
}
