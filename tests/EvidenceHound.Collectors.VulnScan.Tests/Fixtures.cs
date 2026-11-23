namespace EvidenceHound.Collectors.VulnScan.Tests;

internal static class Fixtures
{
    public const string Nessus = "internet-facing-2026-11-16.nessus";

    public const string Defender = "defender-vulnerabilities-2026-11-20.csv";

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "vuln-scan", name));

    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vuln-scan", name));

    /// <summary>The column map the docs give for a Microsoft Defender vulnerability management export.</summary>
    public static ColumnMap DefenderMap { get; } = new("DeviceName", "CveId", "VulnerabilitySeverityLevel")
    {
        Name = "SoftwareName",
        Cve = "CveId",
        FirstSeen = "FirstSeenTimestamp",
        ExploitAvailable = "ExploitabilityLevel",
        ScanDate = "LastSeenTimestamp",
    };
}
