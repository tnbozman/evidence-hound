namespace EvidenceHound.Collectors.Testing;

/// <summary>HTTP clients that never reach the network: every request fails. The default for a <see cref="CollectorScenario"/>.</summary>
public sealed class OfflineHttpClients : IHttpClientFactory
{
    public static OfflineHttpClients Instance { get; } = new();

    public HttpClient CreateClient(string name) => new(new OfflineHandler(), disposeHandler: true);

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"The contract kit is offline, so {request.Method} {request.RequestUri} was not sent. Give the scenario a RecordedApi's HttpClients.");
    }
}
