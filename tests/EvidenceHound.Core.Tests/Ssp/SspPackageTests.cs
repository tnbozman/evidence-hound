using System.IO.Compression;
using System.Text;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-05: what <c>hound ssp publish</c> uploads as one evidence revision: the signed plan, its POA&amp;M, its snapshot and CISO Assistant's audit report.</summary>
public class SspPackageTests
{
    [Fact]
    public void Package_holds_plan_poam_snapshot_and_report_and_is_reproducible()
    {
        var report = new EvidenceFile("EX-E8-2026-audit-report.docx", new byte[] { 1, 2, 3 });

        var package = SspPackage.Zip("essential-eight", "1.0", "# plan\n", "# poam\n", "{}\n", report);

        package.FileName.ShouldBe("ssp-essential-eight-1.0.zip");
        using var zip = new ZipArchive(new MemoryStream(package.Content.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).ShouldBe(["ssp-essential-eight-1.0.md", "poam-essential-eight-1.0.md", "ssp-essential-eight-1.0.snapshot.json", "EX-E8-2026-audit-report.docx"]);
        using (var plan = new StreamReader(zip.GetEntry("ssp-essential-eight-1.0.md")!.Open(), Encoding.UTF8))
        {
            plan.ReadToEnd().ShouldBe("# plan\n");
        }

        SspPackage.Zip("essential-eight", "1.0", "# plan\n", "# poam\n", "{}\n", report).Content.ToArray().ShouldBe(package.Content.ToArray());
    }

    [Fact]
    public void Package_without_a_report_still_holds_the_plan()
    {
        var package = SspPackage.Zip("essential-eight", "1.0", "# plan\n", "# poam\n", "{}\n", null);

        using var zip = new ZipArchive(new MemoryStream(package.Content.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Count.ShouldBe(3);
    }
}
