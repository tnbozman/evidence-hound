namespace EvidenceHound.IacDevServer;

/// <summary>A request the dev server answered: its path and status.</summary>
public sealed record ServedRequest(string Path, int Status);

/// <summary>Answers the GitHub Actions API for an <see cref="IacDevPipeline"/>.</summary>
public sealed class DevIacServer : IDisposable
{
    public Uri Url => throw new NotImplementedException();

    public IHttpClientFactory HttpClients => throw new NotImplementedException();

    public IReadOnlyList<ServedRequest> Requests => throw new NotImplementedException();

    public static DevIacServer Start(IacDevPipeline pipeline, DateTimeOffset now, int? port = null, Uri? publicUrl = null) => throw new NotImplementedException();

    public void Dispose()
    {
    }
}
