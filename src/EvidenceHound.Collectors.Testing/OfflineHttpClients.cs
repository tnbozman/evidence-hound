namespace EvidenceHound.Collectors.Testing;

/// <summary>HTTP clients that never reach the network: every request fails. The default for a <see cref="CollectorScenario"/>.</summary>
public sealed class OfflineHttpClients : IHttpClientFactory
{
    public static OfflineHttpClients Instance { get; } = new();

    public HttpClient CreateClient(string name) => throw new NotImplementedException();
}
