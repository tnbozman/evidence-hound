using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

internal static class ScanProblems
{
    public const string ExportInvalid = "vuln_scan.export_invalid";

    public static Problem Invalid(string fileName, int line, string why) =>
        new(ExportInvalid, string.Create(CultureInfo.InvariantCulture, $"{fileName} line {line}: {why}"));
}
