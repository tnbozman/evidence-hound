namespace EvidenceHound.SophosDevServer;

public sealed record ServedRequest(string Path, int Status);

public sealed class DevSophosServer : IDisposable
{
    public Uri Url => throw new NotImplementedException();

    public IHttpClientFactory HttpClients => throw new NotImplementedException();

    public IReadOnlyList<ServedRequest> Requests => throw new NotImplementedException();

    public static DevSophosServer Start(SophosDevTenant tenant, int? port = null, Uri? publicUrl = null) => throw new NotImplementedException();

    public void Dispose()
    {
    }
}
