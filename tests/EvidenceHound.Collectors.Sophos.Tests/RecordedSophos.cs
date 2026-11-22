using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.Collectors.Sophos.Tests;

/// <summary>
/// A fictional <c>example.com</c> Sophos Central tenant replayed from the recorded responses in <c>tests/fixtures/sophos</c>.
/// Three servers stand in for Sophos ID, the global API and the tenant's data region. <c>{{api}}</c> and <c>{{region}}</c> in a
/// file stand for the global and region servers' addresses. A list's file <c>&lt;name&gt;.json</c> is its first page and
/// <c>&lt;name&gt;.N.json</c> its page N.
/// </summary>
internal sealed class RecordedSophos : IDisposable
{
    public static readonly string TenantId = "00000000-0000-4000-8000-0000000050a1";

    public static readonly string PartnerId = "00000000-0000-4000-8000-0000000050b1";

    public static readonly string OrganizationId = "00000000-0000-4000-8000-0000000050c1";

    public static readonly string ClientId = "00000000-0000-4000-8000-0000000050d1";

    public static readonly string ClientSecret = "example-sophos-client-secret-4b7d2e";

    public static readonly string Token = "fake-sophos-token-9c2e7a41b3d0";

    public static readonly string SupportedFirmware = "20.0, 21.0";

    private readonly RecordedApi _id = RecordedApi.Start();

    private readonly RecordedApi _api = RecordedApi.Start();

    private readonly RecordedApi _region = RecordedApi.Start();

    private RecordedSophos(string credential, Func<string, JsonArray, JsonArray>? edit)
    {
        _id.Server.Given(Request.Create().WithPath("/api/v2/oauth2/token").UsingPost()).AtPriority(10)
            .RespondWith(Json(200, Read("token.json")));
        _api.Server.Given(Request.Create().WithPath("/whoami/v1").UsingGet()).AtPriority(10)
            .RespondWith(Json(200, Read($"whoami-{credential}.json")));
        _api.Server.Given(Request.Create().WithPath($"/partner/v1/tenants/{TenantId}").UsingGet().WithHeader("X-Partner-ID", PartnerId)).AtPriority(10)
            .RespondWith(Json(200, Read("tenant.json")));
        _api.Server.Given(Request.Create().WithPath($"/organization/v1/tenants/{TenantId}").UsingGet().WithHeader("X-Organization-ID", OrganizationId)).AtPriority(10)
            .RespondWith(Json(200, Read("tenant.json")));
        Lists(edit);
    }

    public Uri IdUrl => _id.Url;

    public Uri ApiUrl => _api.Url;

    public Uri RegionUrl => _region.Url;

    public WireMock.Server.WireMockServer Id => _id.Server;

    public WireMock.Server.WireMockServer Api => _api.Server;

    public WireMock.Server.WireMockServer Region => _region.Server;

    /// <summary>HTTP clients with no base address: the collector always sends absolute addresses.</summary>
    public IHttpClientFactory HttpClients { get; } = new PlainClients();

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "sophos");

    /// <summary>Starts a tenant whose credential is a <paramref name="credential"/> one (tenant, partner or organization).</summary>
    public static RecordedSophos Start(string credential = "tenant", Func<string, JsonArray, JsonArray>? edit = null) => new(credential, edit);

    /// <summary>Every recorded list's items, all pages merged, for pure tests that need no server.</summary>
    public static Dictionary<string, JsonArray> Items() => new(StringComparer.Ordinal)
    {
        ["endpoints"] = Merge(Pages("endpoints", "https://api.example.test/", "https://api-r1.example.test/")),
        ["firewalls"] = Merge(Pages("firewalls", "https://api.example.test/", "https://api-r1.example.test/")),
    };

    /// <summary>Settings for a collector instance that reads this tenant.</summary>
    public Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        ["client_id"] = ClientId,
        ["id_url"] = IdUrl.ToString(),
        ["api_url"] = ApiUrl.ToString(),
        ["supported_firewall_firmware"] = SupportedFirmware,
    };

    /// <summary>Sophos ID refuses the credential, as it does for a wrong client ID or secret.</summary>
    public RecordedSophos RefuseToken()
    {
        _id.Server.Given(Request.Create().WithPath("/api/v2/oauth2/token").UsingPost()).AtPriority(0)
            .RespondWith(Json(401, new JsonObject { ["errorCode"] = "oauth.invalid_client_secret", ["message"] = "Invalid client secret", ["trackingId"] = "00000000-0000-4000-8000-0000000071a2" }.ToJsonString()));
        return this;
    }

    /// <summary>Answers <paramref name="path"/> on the region server with an error.</summary>
    public RecordedSophos FailRegion(string path, int status, string error, string message)
    {
        _region.Server.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(0)
            .RespondWith(Json(status, new JsonObject { ["error"] = error, ["message"] = message }.ToJsonString()));
        return this;
    }

    /// <summary>Answers whoami with <paramref name="body"/> instead of the recording.</summary>
    public RecordedSophos Whoami(JsonObject body)
    {
        _api.Server.Given(Request.Create().WithPath("/whoami/v1").UsingGet()).AtPriority(0).RespondWith(Json(200, body.ToJsonString()));
        return this;
    }

    public void Dispose()
    {
        _id.Dispose();
        _api.Dispose();
        _region.Dispose();
    }

    private void Lists(Func<string, JsonArray, JsonArray>? edit)
    {
        foreach (var (name, path) in new[] { ("endpoints", "/endpoint/v1/endpoints"), ("firewalls", "/firewall/v1/firewalls") })
        {
            var pages = Pages(name, ApiUrl.ToString(), RegionUrl.ToString());
            if (edit is not null)
            {
                var only = new JsonObject { ["items"] = edit(name, Merge(pages)) };
                only["pages"] = name == "endpoints"
                    ? new JsonObject { ["size"] = only["items"]!.AsArray().Count, ["maxSize"] = 500 }
                    : new JsonObject { ["current"] = 1, ["total"] = 1, ["size"] = 100, ["items"] = only["items"]!.AsArray().Count, ["maxSize"] = 100 };
                pages = [only];
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var request = Request.Create().WithPath(path).UsingGet().WithHeader("X-Tenant-ID", TenantId).WithHeader("Authorization", $"Bearer {Token}");
                request = i == 0 ? request : name == "endpoints"
                    ? request.WithParam("pageFromKey", pages[i]["pages"]!["fromKey"]!.GetValue<string>())
                    : request.WithParam("page", $"{i + 1}");
                _region.Server.Given(request).AtPriority(i == 0 ? 10 : 1).RespondWith(Json(200, pages[i].ToJsonString()));
            }
        }
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Folder, file));

    private IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(Substitute(body, ApiUrl.ToString(), RegionUrl.ToString()));

    private static string Substitute(string text, string api, string region) =>
        text.Replace("{{api}}", api.TrimEnd('/'), StringComparison.Ordinal).Replace("{{region}}", region.TrimEnd('/'), StringComparison.Ordinal);

    private static List<JsonObject> Pages(string name, string api, string region)
    {
        var files = Directory.GetFiles(Folder, $"{name}*.json")
            .Where(f => Path.GetFileName(f) == $"{name}.json" || System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), $"^{name}\\.[0-9]+\\.json$"))
            .Order(StringComparer.Ordinal);
        return [.. files.Select(f => JsonNode.Parse(Substitute(File.ReadAllText(f), api, region))!.AsObject())];
    }

    private static JsonArray Merge(IEnumerable<JsonObject> pages) => [.. pages.SelectMany(p => p["items"]!.AsArray()).Select(i => i?.DeepClone())];

    private sealed class PlainClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
