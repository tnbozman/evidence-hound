using System.Text;

namespace EvidenceHound.Collectors.VulnScan.Tests;

public class ScanFolderTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Newest_export_is_current_scan()
    {
        ExportFile[] files = [Export("b.csv", "older", 1), Export("a.csv", "newest", 20), Export("c.csv", "middle", 10)];

        var folder = ScanFolder.Read(files, Parse).Value();

        folder.Current!.FileName.ShouldBe("a.csv");
        folder.History.Select(r => r.FileName).ShouldBe(["c.csv", "b.csv"]);
    }

    [Fact]
    public void The_scan_date_inside_the_export_decides_not_the_file_date()
    {
        ExportFile[] files = [Export("copied-later.csv", "scanned day 3", 30, scanDay: 3), Export("scanned-later.csv", "scanned day 9", 10, scanDay: 9)];

        ScanFolder.Read(files, Parse).Value().Current!.FileName.ShouldBe("scanned-later.csv");
    }

    [Fact]
    public void The_same_content_dropped_twice_is_read_once_under_its_newest_name()
    {
        ExportFile[] files = [Export("scan.csv", "same", 1), Export("scan-copy.csv", "same", 5)];

        var folder = ScanFolder.Read(files, Parse).Value();

        folder.Current!.FileName.ShouldBe("scan-copy.csv");
        folder.History.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_folder_has_no_current_scan()
    {
        var folder = ScanFolder.Read([], Parse).Value();

        folder.Current.ShouldBeNull();
        folder.History.ShouldBeEmpty();
    }

    [Fact]
    public void One_malformed_export_fails_the_whole_folder()
    {
        ExportFile[] files = [Export("good.csv", "fine", 1), Export("bad.csv", "broken", 2)];

        ScanFolder.Read(files, f => f.FileName == "bad.csv"
                ? Core.Results.Result.Fail<ScanRun>(new Core.Results.Problem("vuln_scan.export_invalid", "bad.csv line 3: broken."))
                : Parse(f))
            .Problems().ShouldHaveSingleItem().Message.ShouldBe("bad.csv line 3: broken.");
    }

    private static ExportFile Export(string name, string content, int writtenDay, int? scanDay = null) =>
        new(name, Encoding.UTF8.GetBytes($"{content}|{scanDay ?? writtenDay}"), Day.AddDays(writtenDay));

    private static Core.Results.Result<ScanRun> Parse(ExportFile file)
    {
        var scanDay = int.Parse(Encoding.UTF8.GetString(file.Content.Span).Split('|')[1], System.Globalization.CultureInfo.InvariantCulture);
        return Core.Results.Result.Ok(new ScanRun(file.FileName, Day.AddDays(scanDay), [], []));
    }
}
