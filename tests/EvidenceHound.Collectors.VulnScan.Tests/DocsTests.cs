namespace EvidenceHound.Collectors.VulnScan.Tests;

/// <summary>The operator guide, docs/collectors/vuln-scan.md, keeps up with the collector.</summary>
public class DocsTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "vuln-scan.md"));

    private static readonly VulnScanCollector Collector = new();

    [Fact]
    public void Every_check_id_declared_in_describe_is_documented()
    {
        Collector.Describe().Checks.Where(id => !Guide.Contains($"`{id}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_setting_is_documented()
    {
        Collector.Describe().Settings.Select(s => s.Name).Where(name => !Guide.Contains($"`{name}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_evidence_file_and_problem_code_is_documented()
    {
        string[] names =
        [
            VulnScanCollector.ScanFile,
            VulnScanCollector.HistoryFile,
            PatchChecks.AssessmentFile,
            ScanProblems.ExportInvalid,
            "vuln_scan.drop_folder_missing",
            "vuln_scan.setting_missing",
            "vuln_scan.setting_invalid",
        ];

        names.Where(name => !Guide.Contains($"`{name}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_guide_says_how_to_schedule_exports_from_common_scanners()
    {
        var section = Section("## Scheduling exports from common scanners");

        section.ShouldContain("Nessus");
        section.ShouldContain("Tenable");
        section.ShouldContain("Defender");
        section.ShouldContain("`scan_interval_days`");
    }

    [Fact]
    public void The_guide_names_both_checks_mapped_to_ism_1694()
    {
        var mapping = Section("## In the Essential Eight mapping");

        mapping.ShouldContain("ISM-1694");
        mapping.ShouldContain("`network.firewall.firmware_current`");
        mapping.ShouldNotContain("will take");
    }

    private static string Section(string heading)
    {
        var start = Guide.IndexOf(heading + "\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"The guide has no '{heading}' section.");
        var end = Guide.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? Guide[start..] : Guide[start..end];
    }
}
