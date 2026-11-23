using EvidenceHound.Collectors.Testing;
using EvidenceHound.Collectors.VulnScan;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in vuln-scan collector passes the collector contract, reading a drop folder with one fictional Nessus export.</summary>
public sealed class VulnScanCollectorContractTests : CollectorContractTests<VulnScanCollector>, IDisposable
{
    private readonly string _drop = Directory.CreateTempSubdirectory("hound-vuln-scan-contract-").FullName;

    public VulnScanCollectorContractTests() => File.WriteAllText(Path.Combine(_drop, "internet-facing.nessus"), """
        <NessusClientData_v2><Report name="Example"><ReportHost name="203.0.113.10">
        <HostProperties><tag name="HOST_END">Tue Dec 30 10:00:00 2025</tag><tag name="host-fqdn">web-01.example.com</tag></HostProperties>
        <ReportItem port="443" severity="3" pluginID="900101" pluginName="Example Web Server Multiple Vulnerabilities"><exploit_available>true</exploit_available></ReportItem>
        </ReportHost></Report></NessusClientData_v2>
        """);

    protected override CollectorScenario Working => new()
    {
        Settings = new Dictionary<string, string>
        {
            [VulnScanSettings.DropFolder] = _drop,
            [VulnScanSettings.Format] = "nessus",
            [VulnScanSettings.Scope] = "internet_facing",
            [VulnScanSettings.Owner] = "Sam Example",
        },
    };

    public void Dispose() => Directory.Delete(_drop, recursive: true);

    protected override VulnScanCollector CreateCollector() => new();
}
