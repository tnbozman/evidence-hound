using System.Globalization;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace EvidenceHound.IacDevServer;

/// <summary>A request the dev server answered: its path and status.</summary>
public sealed record ServedRequest(string Path, int Status);

/// <summary>
/// Answers the GitHub Actions API for an <see cref="IacDevPipeline"/>: for each workflow its newest run, the run's artefact by name and
/// the artefact's zip. The bearer token is not checked. Other paths get 404.
/// </summary>
public sealed class DevIacServer : IDisposable
{
    private readonly WireMockServer _server;

    private DevIacServer(WireMockServer server, IacDevPipeline pipeline, DateTimeOffset now, Uri? publicUrl)
    {
        _server = server;
        var local = server.Url ?? throw new InvalidOperationException("WireMock started without a URL.");
        Url = publicUrl ?? new Uri(local.Replace("0.0.0.0", "localhost", StringComparison.Ordinal) + "/");
        var repo = $"/repos/{pipeline.Repository}/actions";
        foreach (var workflow in IacAnswers.Workflows)
        {
            Json($"{repo}/workflows/{workflow.File}/runs", IacAnswers.Runs(pipeline, workflow, now).ToJsonString());
            Json($"{repo}/runs/{workflow.RunId}/artifacts", IacAnswers.Artifacts(pipeline, workflow, now).ToJsonString());
            _server.Given(Request.Create().WithPath($"{repo}/artifacts/{workflow.ArtifactId}/zip").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/zip")
                    .WithBody(IacAnswers.Zip(workflow.Member, IacAnswers.Member(pipeline, workflow, now))));
        }
    }

    /// <summary>The address the collector reaches the server at: its <c>api_url</c>.</summary>
    public Uri Url { get; }

    public IHttpClientFactory HttpClients { get; } = new Clients();

    public IReadOnlyList<ServedRequest> Requests =>
        [.. _server.LogEntries.Select(e => new ServedRequest(e.RequestMessage?.Path ?? string.Empty, Status(e.ResponseMessage?.StatusCode)))];

    /// <summary>
    /// Starts serving <paramref name="pipeline"/> with its runs dated from <paramref name="now"/>, on a free local port, or on
    /// <paramref name="port"/> on every interface so a container can reach it at <paramref name="publicUrl"/>.
    /// </summary>
    public static DevIacServer Start(IacDevPipeline pipeline, DateTimeOffset now, int? port = null, Uri? publicUrl = null) =>
        new(
            port is { } fixedPort
                ? WireMockServer.Start(new WireMockServerSettings { Urls = [string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{fixedPort}")] })
                : WireMockServer.Start(),
            pipeline,
            now,
            publicUrl);

    public void Dispose() => _server.Dispose();

    private void Json(string path, string body) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));

    private static int Status(object? code) => code switch
    {
        int number => number,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        _ => 0,
    };

    private sealed class Clients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
