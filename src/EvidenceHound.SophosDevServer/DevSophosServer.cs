using System.Globalization;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace EvidenceHound.SophosDevServer;

/// <summary>A request the dev server answered: its path and status.</summary>
public sealed record ServedRequest(string Path, int Status);

/// <summary>
/// Answers the Sophos Central API for a <see cref="SophosDevTenant"/>: the token address (always the same answer), <c>whoami</c>, and
/// each list in pages of <see cref="PageSize"/> by page number with a page total, as Sophos Central pages them, and the firmware
/// upgrade check for each firewall the request body names. Other paths get 404.
/// </summary>
public sealed class DevSophosServer : IDisposable
{
    public static readonly int PageSize = 2;

    private readonly WireMockServer _server;

    private DevSophosServer(WireMockServer server, SophosDevTenant tenant, Uri? publicUrl)
    {
        _server = server;
        var local = server.Url ?? throw new InvalidOperationException("WireMock started without a URL.");
        Url = publicUrl ?? new Uri(local.Replace("0.0.0.0", "localhost", StringComparison.Ordinal) + "/");
        Answer(Request.Create().WithPath("/api/v2/oauth2/token").UsingPost(), SophosAnswers.TokenAnswer());
        Answer(Request.Create().WithPath("/whoami/v1").UsingGet(), SophosAnswers.Whoami(tenant, Url));
        foreach (var (name, items) in SophosAnswers.Items(tenant))
        {
            Serve(SophosAnswers.Paths[name], items);
        }

        foreach (var firewall in tenant.Firewalls)
        {
            Answer(Request.Create().WithPath(SophosAnswers.UpgradeCheckPath).UsingPost().WithBody((string? body) => body?.Contains(firewall.Id, StringComparison.Ordinal) == true), SophosAnswers.UpgradeCheck(firewall));
        }
    }

    /// <summary>The address the collector reaches the server at: its <c>id_url</c> and <c>api_url</c>.</summary>
    public Uri Url { get; }

    public IHttpClientFactory HttpClients { get; } = new Clients();

    public IReadOnlyList<ServedRequest> Requests =>
        [.. _server.LogEntries.Select(e => new ServedRequest(e.RequestMessage?.Path ?? string.Empty, Status(e.ResponseMessage?.StatusCode)))];

    /// <summary>
    /// Starts serving <paramref name="tenant"/> on a free local port, or on <paramref name="port"/> on every interface so a container can
    /// reach it; <paramref name="publicUrl"/> is the address the collector uses, which <c>whoami</c> names as the data region.
    /// </summary>
    public static DevSophosServer Start(SophosDevTenant tenant, int? port = null, Uri? publicUrl = null) =>
        new(
            port is { } fixedPort
                ? WireMockServer.Start(new WireMockServerSettings { Urls = [string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{fixedPort}")] })
                : WireMockServer.Start(),
            tenant,
            publicUrl);

    public void Dispose() => _server.Dispose();

    private void Serve(string path, JsonArray items)
    {
        var pages = items.Chunk(PageSize).ToArray();
        var total = Math.Max(pages.Length, 1);
        for (var i = 0; i < total; i++)
        {
            var page = new JsonObject
            {
                ["items"] = new JsonArray([.. (pages.Length == 0 ? [] : pages[i]).Select(item => item?.DeepClone())]),
                ["pages"] = new JsonObject { ["current"] = i + 1, ["size"] = PageSize, ["total"] = total, ["items"] = items.Count, ["maxSize"] = 100 },
            };
            var request = Request.Create().WithPath(path).UsingGet();
            request = i == 0 ? request : request.WithParam("page", (i + 1).ToString(CultureInfo.InvariantCulture));
            Answer(request, page, i == 0 ? 10 : 1);
        }
    }

    private void Answer(IRequestBuilder request, JsonObject body, int priority = 5) =>
        _server.Given(request).AtPriority(priority)
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body.ToJsonString()));

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
