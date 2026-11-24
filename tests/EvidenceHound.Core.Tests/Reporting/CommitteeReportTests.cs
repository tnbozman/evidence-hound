using EvidenceHound.Core.Reporting;
using static EvidenceHound.Core.Tests.Reporting.CommitteeFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>s12-05: the monthly security committee report, a function of the month's snapshot and the previous one.</summary>
public class CommitteeReportTests
{
    private static string Golden => Path.Combine(AppContext.BaseDirectory, "golden", "committee-2026-11.md");

    private static string Report => CommitteeReport.Render(NovemberSnapshot, October);

    [Fact]
    public void Renders_expected_markdown_from_fixture()
    {
        Report.ShouldBe(File.ReadAllText(Golden));
    }

    [Fact]
    public void Expiring_exceptions_use_60_day_window()
    {
        var section = Section(Report, "## 4.");

        // Expires 2027-01-10: 41 days after 30 November, so inside the window; 2027-05-01 is outside it; RA-FAX expired on 2026-11-20.
        Part(section, "### Expiring within 60 days").ShouldContain("SE-PRINTER");
        Part(section, "### Expiring within 60 days").ShouldNotContain("RA-KIOSK");
        Part(section, "### Expired").ShouldContain("RA-FAX");
        Part(section, "### New").ShouldContain("RA-KIOSK");
    }

    [Fact]
    public void Overdue_remediation_sorted_by_due_date()
    {
        var overdue = Part(Section(Report, "## 5."), "### Overdue");

        overdue.IndexOf("FND-1", StringComparison.Ordinal).ShouldBeLessThan(overdue.IndexOf("FND-3", StringComparison.Ordinal));
        overdue.ShouldNotContain("FND-4");
        Section(Report, "## 5.").ShouldContain("Completed this month: 1 ([FND-2]");
    }

    [Fact]
    public void Empty_sections_say_none_rather_than_disappear()
    {
        var empty = new PostureSnapshot(ReportingFixtures.November, DateTimeOffset.UnixEpoch, [], [], [], [], [], [], [], []);

        var report = CommitteeReport.Render(empty, null);

        foreach (var heading in new[] { "## 1.", "## 2.", "## 3.", "## 4.", "## 5.", "## 6.", "## 7.", "## 8." })
        {
            Section(report, heading).ShouldContain("None", Case.Sensitive, heading);
        }
    }

    [Fact]
    public void Groups_changes_by_programme_and_parent()
    {
        var section = Section(Report, "## 3.");

        section.IndexOf("### Essential Eight", StringComparison.Ordinal).ShouldBeLessThan(section.IndexOf("### AI governance", StringComparison.Ordinal));
        Part(section, "### Essential Eight").ShouldContain("#### mfa");
        Part(section, "### Essential Eight").ShouldContain("#### rb");
        Part(section, "#### mfa").ShouldContain("[E8-1](https://ciso.example.com/requirement-assessments/e8-1): non-compliant → compliant");
        Part(section, "### AI governance").ShouldContain("No result changed.");
        Part(section, "### AI governance").ShouldContain("Register: 2 ai_systems");
    }

    [Fact]
    public void Owners_overdue_a_second_month_are_listed_first_and_carried_into_actions()
    {
        var due = Section(Report, "## 7.");

        due.IndexOf("Backups: restore test", StringComparison.Ordinal).ShouldBeLessThan(due.IndexOf("Privileged access review", StringComparison.Ordinal));
        due.ShouldContain("second month");
        Section(Report, "## 8.").ShouldContain("Backups: restore test");
        Section(Report, "## 8.").ShouldContain("Example backup owner");
    }

    [Fact]
    public void Sensitive_data_warnings_join_the_actions()
    {
        Section(Report, "## 8.").ShouldContain("data.authority_expired");
    }

    private static string Section(string report, string heading)
    {
        var start = report.IndexOf("\n" + heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, heading);
        var end = report.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return end < 0 ? report[start..] : report[start..end];
    }

    private static string Part(string section, string heading)
    {
        var start = section.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, heading);
        var level = heading.TakeWhile(c => c == '#').Count();
        var next = section.IndexOf("\n" + new string('#', level) + " ", start + 1, StringComparison.Ordinal);
        return next < 0 ? section[start..] : section[start..next];
    }
}
