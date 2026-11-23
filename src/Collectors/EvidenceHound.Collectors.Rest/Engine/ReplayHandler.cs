using System.IO.Compression;
using System.Net;
using System.Text;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// Answers requests from a test case's recordings, offline. A recording answers a request with the same method and path whose
/// query holds every value the recording lists; the most specific one wins. <c>{{api}}</c> in a recorded body becomes the address
/// the request went to, so next links stay on the API's host. A request with no recording is answered 404 and listed.
/// </summary>
internal sealed class ReplayHandler(IReadOnlyList<RecordedResponse> responses, string folder) : HttpMessageHandler
{
    private readonly List<string> _unrecorded = [];

    public IReadOnlyList<string> Unrecorded => _unrecorded;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        var recording = responses
            .Where(r => r.Method == request.Method.Method && r.Path == url.AbsolutePath && r.Query.All(q => query[q.Key] == q.Value))
            .OrderByDescending(r => r.Query.Count)
            .FirstOrDefault();
        if (recording is null)
        {
            _unrecorded.Add($"{request.Method.Method} {url.PathAndQuery}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"no recording"}""", Encoding.UTF8, "application/json") });
        }

        if (recording.File is { } archive && Directory.Exists(Path.Combine(folder, archive)))
        {
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)recording.Status) { Content = Zipped(Path.Combine(folder, archive)) });
        }

        var body = (recording.File is { } file ? File.ReadAllText(Path.Combine(folder, file)) : recording.Body ?? "")
            .Replace("{{api}}", url.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)recording.Status) { Content = new StringContent(body, Encoding.UTF8, MediaType(recording.File)) });
    }

    /// <summary>A recording that is a folder (such as <c>recordings/plan.zip/</c>) is answered as a zip of its files, so recorded artefacts stay readable in review.</summary>
    private static ByteArrayContent Zipped(string directory)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                using var entry = zip.CreateEntry(Path.GetRelativePath(directory, path).Replace('\\', '/'), CompressionLevel.Optimal).Open();
                using var source = File.OpenRead(path);
                source.CopyTo(entry);
            }
        }

        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        return content;
    }

    private static string MediaType(string? file) => Path.GetExtension(file ?? "").ToUpperInvariant() switch
    {
        ".XML" => "application/xml",
        ".CSV" => "text/csv",
        _ => "application/json",
    };
}
