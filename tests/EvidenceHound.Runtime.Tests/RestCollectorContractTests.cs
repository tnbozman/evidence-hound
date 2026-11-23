using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Rest.Tests;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in rest collector passes the collector contract with the example library profile against the recorded example API.</summary>
public sealed class RestCollectorContractTests : CollectorContractTests<RestCollector>, IDisposable
{
    private readonly string _profiles = Directory.CreateTempSubdirectory("hound-rest-contract-").FullName;

    private readonly RecordedExample _api = RecordedExample.Start();

    public RestCollectorContractTests() =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "files", "profiles", "example.yaml"), Path.Combine(_profiles, "example.yaml"));

    protected override CollectorScenario Working => new()
    {
        Settings = _api.Settings(_profiles),
        Secrets = new Dictionary<string, string> { ["client_secret"] = RecordedExample.ClientSecret },
        HttpClients = RecordedExample.HttpClients,
    };

    public void Dispose()
    {
        _api.Dispose();
        Directory.Delete(_profiles, recursive: true);
    }

    protected override RestCollector CreateCollector() => new();
}
