using EvidenceHound.Collectors.Manual;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in manual collector passes the collector contract, reading a drop folder with one attested export.</summary>
public sealed class ManualCollectorContractTests : CollectorContractTests<ManualCollector>, IDisposable
{
    private readonly string _drop = Directory.CreateTempSubdirectory("hound-manual-contract-").FullName;

    public ManualCollectorContractTests()
    {
        // The contract kit's clock reads 2026-01-01, so evidence from the day before is within the interval.
        var export = Path.Combine(_drop, "firewall-config.txt");
        File.WriteAllText(export, "rule 1: allow https from any to web-01.example.com");
        File.SetLastWriteTimeUtc(export, new DateTime(2025, 12, 31, 9, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(export + Attestation.Suffix, """
            who: Alex Example
            date: 2025-12-31
            outcomes:
              network.firewall.rules_reviewed: pass
            """);
    }

    protected override CollectorScenario Working => new()
    {
        Settings = new Dictionary<string, string>
        {
            [ManualSettings.DropFolder] = _drop,
            [ManualSettings.Patterns] = "*.txt",
            [ManualSettings.Checks] = "network.firewall.rules_reviewed, network.firewall.config_exported",
            [ManualSettings.DueDays] = "30",
            [ManualSettings.Owner] = "Alex Example",
        },
    };

    public void Dispose() => Directory.Delete(_drop, recursive: true);

    protected override ManualCollector CreateCollector() => new();
}
