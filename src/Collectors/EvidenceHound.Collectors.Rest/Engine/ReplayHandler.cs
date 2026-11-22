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

        var body = (recording.File is { } file ? File.ReadAllText(Path.Combine(folder, file)) : recording.Body ?? "")
            .Replace("{{api}}", url.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)recording.Status) { Content = new StringContent(body, Encoding.UTF8, MediaType(recording.File)) });
    }

    private static string MediaType(string? file) => Path.GetExtension(file ?? "").ToUpperInvariant() switch
    {
        ".XML" => "application/xml",
        ".CSV" => "text/csv",
        _ => "application/json",
    };
}
