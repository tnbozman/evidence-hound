namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// Which CSV column holds each field of a finding, by header name. Host, finding ID and severity are required; a field left
/// null is not in the export.
/// </summary>
public sealed record ColumnMap(string Host, string FindingId, string Severity)
{
    public string? Name { get; init; }

    public string? Cve { get; init; }

    public string? FirstSeen { get; init; }

    public string? PatchPublished { get; init; }

    public string? ExploitAvailable { get; init; }

    public string? Unsupported { get; init; }

    public string? ScanDate { get; init; }

    /// <summary>Each mapped column with the setting that names it, required ones first.</summary>
    internal IEnumerable<(string Setting, string Column)> Mapped() =>
        new (string Setting, string? Column)[]
        {
            (VulnScanSettings.ColumnHost, Host),
            (VulnScanSettings.ColumnFindingId, FindingId),
            (VulnScanSettings.ColumnSeverity, Severity),
            (VulnScanSettings.ColumnName, Name),
            (VulnScanSettings.ColumnCve, Cve),
            (VulnScanSettings.ColumnFirstSeen, FirstSeen),
            (VulnScanSettings.ColumnPatchPublished, PatchPublished),
            (VulnScanSettings.ColumnExploitAvailable, ExploitAvailable),
            (VulnScanSettings.ColumnUnsupported, Unsupported),
            (VulnScanSettings.ColumnScanDate, ScanDate),
        }.Where(m => m.Column is not null).Select(m => (m.Setting, m.Column!));
}
