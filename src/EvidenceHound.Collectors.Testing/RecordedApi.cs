using WireMock.Server;

namespace EvidenceHound.Collectors.Testing;

/// <summary>
/// A local HTTP server that replays recorded responses from a target system, built on WireMock.Net. Hand
/// <see cref="HttpClients"/> to the collector through a <see cref="CollectorScenario"/>; use <see cref="Server"/> for anything
/// the helpers do not cover. Requests nothing was recorded for get 404.
/// </summary>
public sealed class RecordedApi : IDisposable
{
    private RecordedApi(WireMockServer server) => Server = server;

    public WireMockServer Server { get; }

    /// <summary>The server's base address.</summary>
    public Uri Url => throw new NotImplementedException();

    /// <summary>HTTP clients whose base address is <see cref="Url"/>, so collectors can use relative paths.</summary>
    public IHttpClientFactory HttpClients => throw new NotImplementedException();

    public static RecordedApi Start() => throw new NotImplementedException();

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with <paramref name="status"/> and <paramref name="body"/>.</summary>
    public RecordedApi Respond(string method, string path, int status, string body, string mediaType = "application/json") =>
        throw new NotImplementedException();

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with the content of a recorded response file.</summary>
    public RecordedApi RespondWithFile(string method, string path, string responseFile, int status = 200, string mediaType = "application/json") =>
        throw new NotImplementedException();

    public void Dispose() => Server.Dispose();
}
