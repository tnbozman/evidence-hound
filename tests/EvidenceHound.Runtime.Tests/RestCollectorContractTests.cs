using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in rest collector passes the collector contract with the example library profile.</summary>
public sealed class RestCollectorContractTests : CollectorContractTests<RestCollector>, IDisposable
{
    private readonly string _profiles = Directory.CreateTempSubdirectory("hound-rest-contract-").FullName;

    public RestCollectorContractTests() =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "files", "profiles", "example.yaml"), Path.Combine(_profiles, "example.yaml"));

    protected override CollectorScenario Working => new()
    {
        Settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profile"] = "example",
            ["profiles_path"] = _profiles,
            ["client_id"] = "00000000-0000-4000-8000-0000000000c1",
        },
    };

    public void Dispose() => Directory.Delete(_profiles, recursive: true);

    protected override RestCollector CreateCollector() => new();
}
