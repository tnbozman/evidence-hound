using System.Security.Cryptography;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>How severe a scanner rates a finding, from Nessus's 0 to 4.</summary>
public enum Severity
{
    Info,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// One finding on one host, as any scanner reports it. <paramref name="FirstSeen"/> and <paramref name="PatchPublished"/> are null when the
/// export does not say; <paramref name="Unsupported"/> marks software or an operating system its vendor no longer supports.
/// </summary>
public sealed record ScanFinding(
    string Host,
    string FindingId,
    string Name,
    Severity Severity,
    IReadOnlyList<string> Cves,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? PatchPublished,
    bool ExploitAvailable,
    bool Unsupported);

/// <summary>One scan export: when the scan ran, the hosts it covered and what it found on them.</summary>
public sealed record ScanRun(string FileName, DateTimeOffset ScanDate, IReadOnlyList<string> Hosts, IReadOnlyList<ScanFinding> Findings);

/// <summary>One export file found in the drop folder.</summary>
public sealed record ExportFile(string FileName, ReadOnlyMemory<byte> Content, DateTimeOffset ModifiedAt)
{
    public string ContentHash => Convert.ToHexStringLower(SHA256.HashData(Content.Span));
}
