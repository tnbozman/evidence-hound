using System.Text;

namespace EvidenceHound.Collectors.VulnScan.Tests;

public class NessusParserTests
{
    [Fact]
    public void Reads_hosts_findings_and_exploit_flag()
    {
        var run = NessusParser.Parse(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus)).Value();

        run.FileName.ShouldBe(Fixtures.Nessus);
        run.Hosts.ShouldBe(["web-01.example.com", "vpn.example.com", "mail.example.com"]);
        run.Findings.Count.ShouldBe(3);
        var web = run.Findings[0];
        web.Host.ShouldBe("web-01.example.com");
        web.FindingId.ShouldBe("900101");
        web.Name.ShouldBe("Example Web Server 2.4.x < 2.4.62 Multiple Vulnerabilities");
        web.Severity.ShouldBe(Severity.High);
        web.Cves.ShouldBe(["CVE-2026-0101", "CVE-2026-0102"]);
        web.ExploitAvailable.ShouldBeTrue();
        web.PatchPublished.ShouldBe(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        web.FirstSeen.ShouldBeNull();
        web.Unsupported.ShouldBeFalse();
        run.Findings[1].ExploitAvailable.ShouldBeFalse();
        run.Findings[1].Severity.ShouldBe(Severity.Medium);
    }

    [Fact]
    public void Unsupported_by_vendor_is_flagged()
    {
        var vpn = NessusParser.Parse(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus)).Value().Findings.Single(f => f.Host == "vpn.example.com");

        vpn.Unsupported.ShouldBeTrue();
        vpn.Severity.ShouldBe(Severity.Critical);
    }

    [Fact]
    public void Scan_date_is_when_the_last_host_finished()
    {
        var run = NessusParser.Parse(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus)).Value();

        run.ScanDate.ShouldBe(new DateTimeOffset(2026, 11, 16, 10, 25, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Malformed_xml_is_reported_with_file_and_line()
    {
        var problem = NessusParser.Parse("broken.nessus", Encoding.UTF8.GetBytes("<NessusClientData_v2>\n<Report name=\"x\">\n<ReportHost name=\"a\">\n</Report>")).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("vuln_scan.export_invalid");
        problem.Message.ShouldContain("broken.nessus");
        problem.Message.ShouldContain("line 4");
    }

    [Fact]
    public void Finding_without_plugin_id_is_reported_with_its_line()
    {
        var xml = "<NessusClientData_v2>\n<Report name=\"x\">\n<ReportHost name=\"a\">\n<ReportItem severity=\"2\" pluginName=\"x\"/>\n</ReportHost>\n</Report>\n</NessusClientData_v2>";

        var problem = NessusParser.Parse("odd.nessus", Encoding.UTF8.GetBytes(xml)).Problems().ShouldHaveSingleItem();

        problem.Message.ShouldBe("odd.nessus line 4: ReportItem has no pluginID.");
    }

    [Fact]
    public void Document_type_definitions_are_refused()
    {
        var xml = "<?xml version=\"1.0\"?>\n<!DOCTYPE x [<!ENTITY e \"boom\">]>\n<NessusClientData_v2/>";

        NessusParser.Parse("dtd.nessus", Encoding.UTF8.GetBytes(xml)).Problems().ShouldHaveSingleItem().Code.ShouldBe("vuln_scan.export_invalid");
    }

    [Fact]
    public void A_file_that_is_not_a_nessus_export_is_refused()
    {
        NessusParser.Parse("other.nessus", Encoding.UTF8.GetBytes("<html/>")).Problems().ShouldHaveSingleItem().Message
            .ShouldBe("other.nessus line 1: not a Nessus v2 export (root element is html, not NessusClientData_v2).");
    }
}
