namespace EvidenceHound.Collectors.VulnScan.Tests;

public class CsvScanParserTests
{
    private static readonly DateTimeOffset Written = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Column_map_reads_defender_export()
    {
        var run = CsvScanParser.Parse(Fixtures.Defender, Fixtures.Text(Fixtures.Defender), Fixtures.DefenderMap, Written).Value();

        run.Hosts.ShouldBe(["laptop-01.example.com", "laptop-02.example.com"]);
        run.Findings.Count.ShouldBe(3);
        var reader = run.Findings[0];
        reader.Host.ShouldBe("laptop-01.example.com");
        reader.FindingId.ShouldBe("CVE-2026-0201");
        reader.Name.ShouldBe("example-pdf-reader");
        reader.Cves.ShouldBe(["CVE-2026-0201"]);
        reader.Severity.ShouldBe(Severity.Critical);
        reader.ExploitAvailable.ShouldBeTrue();
        reader.FirstSeen.ShouldBe(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));
        reader.PatchPublished.ShouldBeNull();
        reader.Unsupported.ShouldBeFalse();
        run.Findings[1].ExploitAvailable.ShouldBeFalse();
        run.ScanDate.ShouldBe(new DateTimeOffset(2026, 11, 20, 6, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Missing_mapped_column_is_reported_with_line()
    {
        var map = Fixtures.DefenderMap with { PatchPublished = "PatchReleaseDate" };

        var problem = CsvScanParser.Parse(Fixtures.Defender, Fixtures.Text(Fixtures.Defender), map, Written).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("vuln_scan.export_invalid");
        problem.Message.ShouldBe($"{Fixtures.Defender} line 1: no column 'PatchReleaseDate' (setting column_patch_published).");
    }

    [Fact]
    public void Unreadable_value_is_reported_with_its_line()
    {
        var csv = "host,id,severity,seen\nweb-01.example.com,A-1,High,2026-11-01\nweb-02.example.com,A-2,Severe,2026-11-02\nweb-03.example.com,A-3,Low,yesterday\n";
        var map = new ColumnMap("host", "id", "severity") { FirstSeen = "seen" };

        var problems = CsvScanParser.Parse("scan.csv", csv, map, Written).Problems();

        problems.Select(p => p.Message).ShouldBe([
            "scan.csv line 3: severity 'Severe' is not one of info, low, medium, high, critical (or 0 to 4).",
            "scan.csv line 4: first seen 'yesterday' is not a date.",
        ]);
    }

    [Fact]
    public void Without_a_scan_date_column_the_file_date_is_the_scan_date()
    {
        var csv = "host,id,severity,unsupported\nweb-01.example.com,A-1,4,yes\n";
        var map = new ColumnMap("host", "id", "severity") { Unsupported = "unsupported" };

        var run = CsvScanParser.Parse("scan.csv", csv, map, Written).Value();

        run.ScanDate.ShouldBe(Written);
        run.Findings.ShouldHaveSingleItem().Severity.ShouldBe(Severity.Critical);
        run.Findings[0].Unsupported.ShouldBeTrue();
        run.Findings[0].Name.ShouldBe("A-1");
        run.Findings[0].Cves.ShouldBeEmpty();
    }

    [Fact]
    public void Row_with_the_wrong_number_of_cells_is_reported_with_its_line()
    {
        CsvScanParser.Parse("scan.csv", "host,id,severity\nweb-01.example.com,A-1\n", new ColumnMap("host", "id", "severity"), Written)
            .Problems().ShouldHaveSingleItem().Message.ShouldBe("scan.csv line 2: 2 cells, but the header has 3.");
    }
}
