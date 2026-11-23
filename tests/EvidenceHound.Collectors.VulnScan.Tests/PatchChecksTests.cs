using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.VulnScan.Tests;

public class PatchChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 7, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Scanned = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static VulnScanSource Source(string scope = "internet_facing") => new("/drop", ScanFormat.Nessus, scope, "Sam Example", null) { Policy = PatchPolicy.For(scope) };

    [Fact]
    public void Policy_defaults_follow_maturity_level_two_for_each_scope()
    {
        PatchPolicy.For("internet_facing").ShouldBe(new PatchPolicy(1, 14, 48));
        PatchPolicy.For("common_apps").ShouldBe(new PatchPolicy(7, 14, 336));
        PatchPolicy.For("other_apps").ShouldBe(new PatchPolicy(14, 30, 720));
        PatchPolicy.For("operating_systems").ShouldBe(new PatchPolicy(14, 30, 720));
    }

    [Fact]
    public void Check_ids_are_per_scope()
    {
        PatchChecks.Ids("common_apps").ShouldBe(["vuln.common_apps.scanned_in_interval", "vuln.common_apps.patched_in_window", "vuln.common_apps.unsupported_removed"]);
        PatchChecks.Ids("other_apps").ShouldBe(["vuln.other_apps.scanned_in_interval", "vuln.other_apps.patched_in_window"]);
        PatchChecks.All.Count.ShouldBe(11);
    }

    [Fact]
    public void Scan_within_interval_passes()
    {
        var finding = Check(Assess(Folder(Run(Scanned))), "scanned_in_interval");

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Scanned 2026-11-23 (scan.nessus): within the 1-day interval for internet_facing.");
        finding.EvidenceRefs.ShouldBe(["scan.nessus.xml", VulnScanCollector.ScanFile]);
    }

    [Fact]
    public void Scan_older_than_interval_fails()
    {
        var finding = Check(Assess(Folder(Run(Scanned.AddDays(-3)))), "scanned_in_interval");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("Last scan 2026-11-20 (scan.nessus) is 3 days old; internet_facing must be scanned at least every 1 day. Owner: Sam Example.");
    }

    [Fact]
    public void No_scan_yet_fails_the_interval_and_cannot_assess_the_rest()
    {
        var checks = Assess(new ScanFolder(null, []));

        Check(checks, "scanned_in_interval").Outcome.ShouldBe(CheckOutcome.Fail);
        Check(checks, "scanned_in_interval").Observation.ShouldBe("No scan export in the drop folder yet; internet_facing must be scanned at least every 1 day. Owner: Sam Example.");
        Check(checks, "patched_in_window").Outcome.ShouldBe(CheckOutcome.Error);
        Check(checks, "unsupported_removed").Observation.ShouldBe("Unable to assess: no scan export in the drop folder yet.");
    }

    [Fact]
    public void Findings_within_their_window_pass()
    {
        var finding = Check(Assess(Folder(Run(Scanned, Finding("web-01.example.com", "A-1", published: Scanned.AddDays(-13))))), "patched_in_window");

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("No open finding in the 2026-11-23 scan (scan.nessus) is past its window (14 days, or 48 hours with an exploit): 1 finding on 1 host.");
        finding.EvidenceRefs.ShouldBe(["scan.nessus.xml", VulnScanCollector.ScanFile, PatchChecks.AssessmentFile]);
    }

    [Fact]
    public void Exploited_finding_uses_hours_window()
    {
        var finding = Check(
            Assess(Folder(Run(Scanned, Finding("web-01.example.com", "A-1", published: Scanned.AddHours(-50), exploit: true), Finding("web-01.example.com", "A-2", published: Scanned.AddHours(-50))))),
            "patched_in_window");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("1 of 2 open findings in the 2026-11-23 scan (scan.nessus) are past their window (14 days, or 48 hours with an exploit): web-01.example.com A-1 (Finding A-1, exploit available, 2 days since the patch was published).");
    }

    [Fact]
    public void Window_counts_from_patch_publication_when_known()
    {
        var finding = Check(
            Assess(Folder(Run(Scanned, Finding("web-01.example.com", "A-1", published: Scanned.AddDays(-20), firstSeen: Scanned.AddDays(-2))))),
            "patched_in_window");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("web-01.example.com A-1 (Finding A-1, 20 days since the patch was published)");
    }

    [Fact]
    public void Without_a_patch_date_the_window_counts_from_first_seen()
    {
        var finding = Check(Assess(Folder(Run(Scanned, Finding("laptop-01.example.com", "CVE-2026-0201", firstSeen: Scanned.AddDays(-15))))), "patched_in_window");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("15 days since first seen");
    }

    [Fact]
    public void Without_either_date_the_window_counts_from_the_earliest_scan_in_the_history()
    {
        var current = Run(Scanned, Finding("web-01.example.com", "A-1"));
        var older = Run(Scanned.AddDays(-16), Finding("web-01.example.com", "A-1")) with { FileName = "older.nessus" };
        var unrelated = Run(Scanned.AddDays(-30), Finding("web-02.example.com", "A-1")) with { FileName = "oldest.nessus" };

        var finding = Check(Assess(new ScanFolder(current, [older, unrelated])), "patched_in_window");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("16 days since first scanned (older.nessus)");
        finding.EvidenceRefs.ShouldContain(VulnScanCollector.HistoryFile);
    }

    [Fact]
    public void Detail_names_at_most_twenty_findings_and_counts_the_rest()
    {
        var findings = Enumerable.Range(1, 23).Select(i => Finding($"ws-{i:00}.example.com", "A-1", published: Scanned.AddDays(-40))).ToArray();

        var finding = Check(Assess(Folder(Run(Scanned, findings))), "patched_in_window");

        finding.Observation.ShouldContain("ws-20.example.com A-1");
        finding.Observation.ShouldNotContain("ws-21.example.com");
        finding.Observation.ShouldEndWith(", and 3 more (all in patch-assessment.json).");
    }

    [Fact]
    public void Unsupported_software_fails_naming_hosts()
    {
        var finding = Check(
            Assess(Folder(Run(Scanned, Finding("vpn.example.com", "900103", unsupported: true), Finding("web-01.example.com", "A-1", published: Scanned)))),
            "unsupported_removed");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("1 unsupported in the 2026-11-23 scan (scan.nessus), on vpn.example.com: vpn.example.com 900103 (Finding 900103).");
    }

    [Fact]
    public void No_unsupported_software_passes()
    {
        var finding = Check(Assess(Folder(Run(Scanned, Finding("web-01.example.com", "A-1", published: Scanned)))), "unsupported_removed");

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Nothing unsupported in the 2026-11-23 scan (scan.nessus) of 1 host.");
    }

    [Fact]
    public void Assessment_lists_every_overdue_and_unsupported_finding()
    {
        var folder = Folder(Run(Scanned, Finding("vpn.example.com", "900103", unsupported: true, published: Scanned.AddDays(-100)), Finding("web-01.example.com", "A-1", published: Scanned)));

        var assessment = PatchChecks.Assessment(Source(), folder);

        assessment.Overdue.Select(o => o.Finding.Host).ShouldBe(["vpn.example.com"]);
        assessment.Overdue[0].AgeDays.ShouldBe(100);
        assessment.Unsupported.Select(f => f.Host).ShouldBe(["vpn.example.com"]);
    }

    private static IReadOnlyList<CheckResult> Assess(ScanFolder folder) => PatchChecks.Assess(Source(), folder, Now, "vuln-example").Value();

    private static CheckResult Check(IReadOnlyList<CheckResult> checks, string name) => checks.Single(c => c.CheckId.Value == $"vuln.internet_facing.{name}");

    private static ScanFolder Folder(ScanRun current) => new(current, []);

    private static ScanRun Run(DateTimeOffset at, params ScanFinding[] findings) =>
        new("scan.nessus", at, [.. findings.Select(f => f.Host).DefaultIfEmpty("web-01.example.com").Distinct(StringComparer.Ordinal)], findings);

    private static ScanFinding Finding(string host, string id, DateTimeOffset? published = null, DateTimeOffset? firstSeen = null, bool exploit = false, bool unsupported = false) =>
        new(host, id, $"Finding {id}", Severity.High, [], firstSeen, published, exploit, unsupported);
}
