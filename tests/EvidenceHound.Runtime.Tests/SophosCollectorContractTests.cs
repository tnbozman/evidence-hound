using EvidenceHound.Collectors.Sophos;
using EvidenceHound.Collectors.Sophos.Tests;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in Sophos Central collector passes the collector contract against the recorded example.com tenant.</summary>
public sealed class SophosCollectorContractTests : CollectorContractTests<SophosCollector>, IDisposable
{
    private readonly RecordedSophos _working = RecordedSophos.Start();

    private readonly RecordedSophos _refusing = RecordedSophos.Start().RefuseToken();

    protected override CollectorScenario Working => new()
    {
        Settings = _working.Settings(),
        Secrets = new Dictionary<string, string> { ["client_secret"] = RecordedSophos.ClientSecret },
        HttpClients = _working.HttpClients,
    };

    protected override CollectorScenario? RefusedCredentials => new() { Settings = _refusing.Settings(), HttpClients = _refusing.HttpClients };

    public void Dispose()
    {
        _working.Dispose();
        _refusing.Dispose();
    }

    protected override SophosCollector CreateCollector() => new();
}
