using EvidenceHound.Collectors.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>
/// The fictional Example Asset API replayed from <c>profiles/example.tests/recordings</c>: a token endpoint, whoami, devices (two pages by
/// next link), users (two pages by offset) and roles. <c>{{api}}</c> in a file stands for the server's address.
/// </summary>
public sealed class RecordedExample : IDisposable
{
    public static readonly string ClientId = "00000000-0000-4000-8000-0000000000c1";

    public static readonly string ClientSecret = "example-rest-client-secret-8f3b1d";

    public static readonly string Token = "fake-example-token-5e1a9c7d2b40";

    private readonly RecordedApi _api = RecordedApi.Start();

    private RecordedExample()
    {
        Server.Given(Request.Create().WithPath("/oauth2/token").UsingPost()).AtPriority(10).RespondWith(Json(200, "token.json"));
        Server.Given(Authorised("/v1/whoami")).AtPriority(10).RespondWith(Json(200, "whoami.json"));
        Server.Given(Authorised("/v1/orgs/org-0001/devices")).AtPriority(10).RespondWith(Json(200, "devices.json"));
        Server.Given(Authorised("/v1/orgs/org-0001/devices").WithParam("cursor", "page-2")).AtPriority(5).RespondWith(Json(200, "devices.2.json"));
        Server.Given(Authorised("/v1/orgs/org-0001/users").WithParam("offset", "0")).AtPriority(10).RespondWith(Json(200, "users.json"));
        Server.Given(Authorised("/v1/orgs/org-0001/users").WithParam("offset", "2")).AtPriority(10).RespondWith(Json(200, "users.2.json"));
        Server.Given(Authorised("/v1/orgs/org-0001/roles")).AtPriority(10).RespondWith(Json(200, "roles.json"));
    }

    public WireMockServer Server => _api.Server;

    public Uri Url => _api.Url;

    /// <summary>Clients that do not follow redirects, as the rest collector's named client is registered.</summary>
    public static IHttpClientFactory HttpClients { get; } = new NoRedirectClients();

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "profiles", "example.tests", "recordings");

    public static RecordedExample Start() => new();

    /// <summary>Settings for an instance of the example library profile that reads this server.</summary>
    public Dictionary<string, string> Settings(string profilesPath) => new(StringComparer.Ordinal)
    {
        ["profile"] = "example",
        ["profiles_path"] = profilesPath,
        ["client_id"] = ClientId,
        ["base_url"] = Url.ToString(),
        ["token_url"] = new Uri(Url, "oauth2/token").ToString(),
        ["supported_os"] = "14, 15",
    };

    /// <summary>The token endpoint refuses the client secret.</summary>
    public RecordedExample RefuseToken()
    {
        Server.Given(Request.Create().WithPath("/oauth2/token").UsingPost()).AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(401).WithHeader("Content-Type", "application/json").WithBody("""{"error":"invalid_client"}"""));
        return this;
    }

    /// <summary>Answers <paramref name="path"/> with <paramref name="status"/> instead of the recording.</summary>
    public RecordedExample Fail(string path, int status)
    {
        Server.Given(Request.Create().WithPath(path)).AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody("""{"error":"recorded failure"}"""));
        return this;
    }

    public void Dispose() => _api.Dispose();

    private static IRequestBuilder Authorised(string path) =>
        Request.Create().WithPath(path).UsingGet().WithHeader("Authorization", $"Bearer {Token}");

    private IResponseBuilder Json(int status, string file) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json")
            .WithBody(File.ReadAllText(Path.Combine(Folder, file)).Replace("{{api}}", Url.ToString().TrimEnd('/'), StringComparison.Ordinal));

    private sealed class NoRedirectClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true);
    }
}
