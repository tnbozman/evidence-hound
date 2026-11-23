namespace EvidenceHound.M365DevServer;

public sealed record ServedRequest(string Path, int Status);

public sealed class DevGraphServer : IDisposable
{
    public Uri Url => throw new NotImplementedException();

    public IHttpClientFactory HttpClients => throw new NotImplementedException();

    public IReadOnlyList<ServedRequest> Requests => throw new NotImplementedException();

    public static DevGraphServer Start(DevTenant tenant, int? port = null) => throw new NotImplementedException();

    public void Dispose()
    {
    }
}
