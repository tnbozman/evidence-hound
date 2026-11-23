using System.Globalization;
using System.Text;
using System.Xml.Linq;
using EvidenceHound.Collectors.VulnScan;

namespace EvidenceHound.ScanDev;

/// <summary>Pure: renders a <see cref="DevScan"/> as the export its scanner would write, dated from <c>today</c>.</summary>
public static class ScanExports
{
    /// <summary>The column map for a Microsoft Defender vulnerability management export, as docs/collectors/vuln-scan.md gives it.</summary>
    public static IReadOnlyList<(string Setting, string Column)> DefenderColumns { get; } =
    [
        (VulnScanSettings.ColumnHost, "DeviceName"),
        (VulnScanSettings.ColumnFindingId, "CveId"),
        (VulnScanSettings.ColumnSeverity, "VulnerabilitySeverityLevel"),
        (VulnScanSettings.ColumnName, "SoftwareName"),
        (VulnScanSettings.ColumnCve, "CveId"),
        (VulnScanSettings.ColumnFirstSeen, "FirstSeenTimestamp"),
        (VulnScanSettings.ColumnExploitAvailable, "ExploitabilityLevel"),
        (VulnScanSettings.ColumnScanDate, "LastSeenTimestamp"),
    ];

    /// <summary>When the scan finished: 02:00 UTC on its day.</summary>
    public static DateTimeOffset ScannedAt(DevScan scan, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return new DateTimeOffset(today.AddDays(-scan.ScannedDaysAgo).ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);
    }

    public static string FileName(DevScan scan, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var day = ScannedAt(scan, today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return scan.Format == ScanFormat.Nessus ? $"{scan.Folder}-{day}.nessus" : $"defender-vulnerabilities-{day}.csv";
    }

    public static string Render(DevScan scan, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return scan.Format == ScanFormat.Nessus ? Nessus(scan, today) : Defender(scan, today);
    }

    private static string Nessus(DevScan scan, DateOnly today)
    {
        var at = ScannedAt(scan, today);
        var network = scan.Scope == "internet_facing" ? "203.0.113" : "192.0.2";
        var hosts = scan.Hosts.Select((host, i) => new XElement(
            "ReportHost",
            new XAttribute("name", string.Create(CultureInfo.InvariantCulture, $"{network}.{10 + i}")),
            new XElement(
                "HostProperties",
                Tag("HOST_END", at.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)),
                Tag("HOST_END_TIMESTAMP", at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                Tag("host-fqdn", host)),
            scan.Findings.Where(f => f.Host == host).Select(f => Item(f, today))));
        var document = new XDocument(new XElement(
            "NessusClientData_v2",
            new XElement("Policy", new XElement("policyName", $"Example Pty Ltd {scan.Folder}")),
            new XElement("Report", new XAttribute("name", $"Example Pty Ltd {scan.Folder} {at:yyyy-MM-dd}"), hosts)));
        return document.Declaration + document.ToString();
    }

    private static XElement Item(DevFinding finding, DateOnly today) => new(
        "ReportItem",
        new XAttribute("port", "443"),
        new XAttribute("protocol", "tcp"),
        new XAttribute("severity", ((int)finding.Severity).ToString(CultureInfo.InvariantCulture)),
        new XAttribute("pluginID", finding.Id),
        new XAttribute("pluginName", finding.Name),
        new XElement("risk_factor", finding.Severity.ToString()),
        new XElement("exploit_available", finding.Exploit ? "true" : "false"),
        finding.PatchDaysAgo is { } days ? new XElement("patch_publication_date", today.AddDays(-days).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)) : null,
        finding.Unsupported ? new XElement("unsupported_by_vendor", "true") : null);

    private static XElement Tag(string name, string value) => new("tag", new XAttribute("name", name), value);

    private static string Defender(DevScan scan, DateOnly today)
    {
        var at = ScannedAt(scan, today);
        var csv = new StringBuilder("DeviceId,DeviceName,OSPlatform,SoftwareVendor,SoftwareName,SoftwareVersion,CveId,VulnerabilitySeverityLevel,RecommendedSecurityUpdate,ExploitabilityLevel,FirstSeenTimestamp,LastSeenTimestamp\n");
        foreach (var finding in scan.Findings)
        {
            var device = string.Create(CultureInfo.InvariantCulture, $"0f1e2d3c4b5a69788796a5b4c3d2e1f0{scan.Hosts.ToList().IndexOf(finding.Host) + 1:00000000}");
            var seen = new DateTimeOffset(today.AddDays(-(finding.FirstSeenDaysAgo ?? 0)).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
            csv.Append(CultureInfo.InvariantCulture, $"{device},{finding.Host},Windows11,example-vendor,{finding.Name},1.0,{finding.Id},{finding.Severity},\"Update {finding.Name}\",")
                .Append(finding.Exploit ? "ExploitIsPublic" : "NoExploit")
                .Append(CultureInfo.InvariantCulture, $",{Iso(seen)},{Iso(at)}\n");
        }

        return csv.ToString();
    }

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
