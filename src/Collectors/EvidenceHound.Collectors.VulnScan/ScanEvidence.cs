using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>Pure: the scan evidence files, as stable JSON.</summary>
internal static class ScanEvidence
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static CollectedEvidence Scan(VulnScanSource source, ScanRun run, string sha256) => File(VulnScanCollector.ScanFile, new JsonObject
    {
        ["scope"] = source.Scope,
        ["owner"] = source.Owner,
        ["file"] = run.FileName,
        ["sha256"] = sha256,
        ["scan_date"] = Date(run.ScanDate),
        ["hosts"] = new JsonArray([.. run.Hosts.Select(h => (JsonNode)JsonValue.Create(h))]),
        ["findings"] = new JsonArray([.. run.Findings.Select(Finding)]),
    });

    public static CollectedEvidence History(VulnScanSource source, IEnumerable<(ScanRun Run, string Sha256)> runs) => File(VulnScanCollector.HistoryFile, new JsonObject
    {
        ["scope"] = source.Scope,
        ["exports"] = new JsonArray([.. runs.Select(r => (JsonNode)new JsonObject
        {
            ["file"] = r.Run.FileName,
            ["sha256"] = r.Sha256,
            ["scan_date"] = Date(r.Run.ScanDate),
            ["hosts"] = r.Run.Hosts.Count,
            ["findings"] = r.Run.Findings.Count,
        })]),
    });

    private static JsonNode Finding(ScanFinding f) => new JsonObject
    {
        ["host"] = f.Host,
        ["finding_id"] = f.FindingId,
        ["name"] = f.Name,
        ["severity"] = f.Severity.ToString().ToLowerInvariant(),
        ["cves"] = new JsonArray([.. f.Cves.Select(c => (JsonNode)JsonValue.Create(c))]),
        ["first_seen"] = f.FirstSeen is { } seen ? Date(seen) : null,
        ["patch_published"] = f.PatchPublished is { } published ? Date(published) : null,
        ["exploit_available"] = f.ExploitAvailable,
        ["unsupported"] = f.Unsupported,
    };

    private static string Date(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static CollectedEvidence File(string name, JsonObject json) =>
        CollectedEvidence.FromBytes(name, "application/json", Encoding.UTF8.GetBytes(json.ToJsonString(Indented)));
}
