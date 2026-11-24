using EvidenceHound.Core.Reporting;
using static EvidenceHound.Core.Tests.Reporting.BoardFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>s12-06: the quarterly board report (governance), a function of the quarter-end snapshots.</summary>
public class BoardReportTests
{
    private static string Report => BoardReport.Render(Q4, QuarterEnds);

    private static string Body => Report[..Report.IndexOf("\n## Appendix", StringComparison.Ordinal)];

    [Fact]
    public void Renders_expected_markdown_from_fixture()
    {
        Report.ShouldBe(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", "board-2026-q4.md")));
    }

    [Fact]
    public void Trend_uses_last_four_quarters()
    {
        var posture = Body[Body.IndexOf("## 2.", StringComparison.Ordinal)..];

        posture.ShouldContain("| 2026-Q1 | 2026-Q2 | 2026-Q3 | 2026-Q4 |");
        posture.ShouldNotContain("2025-Q4");
    }

    [Fact]
    public void One_position_and_posture_table_per_programme()
    {
        var position = Body[Body.IndexOf("## 1.", StringComparison.Ordinal)..Body.IndexOf("## 2.", StringComparison.Ordinal)];
        var posture = Body[Body.IndexOf("## 2.", StringComparison.Ordinal)..Body.IndexOf("## 3.", StringComparison.Ordinal)];

        position.Split('\n').Count(l => l.StartsWith("- **", StringComparison.Ordinal)).ShouldBe(2);
        posture.Split('\n').Count(l => l.StartsWith("### ", StringComparison.Ordinal)).ShouldBe(2);
        posture.ShouldContain("Register: 2 ai_systems");
        // E8-2 went from partially compliant to non-compliant: the Essential Eight is off track, AI6 is on track.
        position.ShouldContain("**Essential Eight: off track**");
        position.ShouldContain("**AI governance (AI6 Foundations): on track**");
    }

    [Fact]
    public void Lists_risk_acceptances_made_in_quarter_with_expiry()
    {
        var section = Body[Body.IndexOf("## 4.", StringComparison.Ordinal)..Body.IndexOf("## 5.", StringComparison.Ordinal)];

        section.ShouldContain("RA-KIOSK");
        section.ShouldContain("2027-05-01");
        section.ShouldContain("SE-CAD");
        section.ShouldNotContain("SE-PRINTER");
        // Accepted for more than 12 months: needs the board's endorsement.
        section.Split('\n').Single(l => l.Contains("SE-CAD", StringComparison.Ordinal)).ShouldContain("**board endorsement**");
    }

    [Fact]
    public void Top_five_risks_by_residual_level()
    {
        var section = Body[Body.IndexOf("## 3.", StringComparison.Ordinal)..Body.IndexOf("## 4.", StringComparison.Ordinal)];

        section.IndexOf("R-2", StringComparison.Ordinal).ShouldBeLessThan(section.IndexOf("R-1", StringComparison.Ordinal));
        section.ShouldNotContain("R-6");
    }

    [Fact]
    public void Excludes_operational_detail_from_body()
    {
        Body.ShouldNotContain("example-sophos");
        Body.ShouldNotContain("Backups: restore test");
        Body.ShouldNotContain("401");
        Report.ShouldContain("example-sophos");
    }
}
