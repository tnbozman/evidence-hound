using EvidenceHound.Collectors.M365;
using EvidenceHound.Collectors.M365.Tests;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in Microsoft 365 collector passes the collector contract against the recorded example.com tenant.</summary>
public sealed class M365CollectorContractTests : CollectorContractTests<M365Collector>, IDisposable
{
    private readonly RecordedTenant _working = RecordedTenant.Start();

    private readonly RecordedTenant _refusing = RecordedTenant.Start().RefuseAll();

    protected override CollectorScenario Working => new() { Settings = _working.Settings(), HttpClients = _working.HttpClients };

    protected override CollectorScenario? RefusedCredentials => new() { Settings = _refusing.Settings(), HttpClients = _refusing.HttpClients };

    public void Dispose()
    {
        _working.Dispose();
        _refusing.Dispose();
    }

    protected override M365Collector CreateCollector() => new(new TestCredentialFactory(), new SystemGraphWait());
}
