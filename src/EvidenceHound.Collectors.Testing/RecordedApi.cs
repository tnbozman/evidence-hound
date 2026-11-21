using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Testing;

/// <summary>
/// A local HTTP server that replays recorded responses from a target system, built on WireMock.Net. Hand
/// <see cref="HttpClients"/> to the collector through a <see cref="CollectorScenario"/>; use <see cref="Server"/> for anything
/// the helpers do not cover. Requests nothing was recorded for get 404.
/// </summary>
public sealed class RecordedApi : IDisposable
{
    private RecordedApi(WireMockServer server)
    {
        Server = server;
        Url = new Uri(server.Url ?? throw new InvalidOperationException("WireMock started without a URL."));
        HttpClients = new BaseAddressClients(Url);
    }

    public WireMockServer Server { get; }

    /// <summary>The server's base address.</summary>
    public Uri Url { get; }

    /// <summary>HTTP clients whose base address is <see cref="Url"/>, so collectors can use relative paths.</summary>
    public IHttpClientFactory HttpClients { get; }

    public static RecordedApi Start() => new(WireMockServer.Start());

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with <paramref name="status"/> and <paramref name="body"/>.</summary>
    public RecordedApi Respond(string method, string path, int status, string body, string mediaType = "application/json")
    {
        Server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", mediaType).WithBody(body));
        return this;
    }

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with the content of a recorded response file.</summary>
    public RecordedApi RespondWithFile(string method, string path, string responseFile, int status = 200, string mediaType = "application/json") =>
        Respond(method, path, status, File.ReadAllText(responseFile), mediaType);

    public void Dispose() => Server.Dispose();

    private sealed class BaseAddressClients(Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseAddress };
    }
}
