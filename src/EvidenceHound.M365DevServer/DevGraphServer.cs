using System.Globalization;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.M365DevServer;

/// <summary>A request the dev server answered: its path and status.</summary>
public sealed record ServedRequest(string Path, int Status);

/// <summary>
/// Answers Microsoft Graph for a <see cref="DevTenant"/> on a local port. Collections come in pages of <see cref="PageSize"/> items
/// with <c>@odata.nextLink</c>, as Graph pages them; paths nothing is served for get 404, as Graph answers an unknown resource.
/// </summary>
public sealed class DevGraphServer : IDisposable
{
    public static readonly int PageSize = 5;

    private readonly WireMockServer _server;

    private DevGraphServer(WireMockServer server, DevTenant tenant)
    {
        _server = server;
        Url = new Uri((server.Url ?? throw new InvalidOperationException("WireMock started without a URL.")) + "/");
        foreach (var (name, items) in GraphCollections.Render(tenant, Url))
        {
            if (GraphCollections.IsHunting(name))
            {
                Hunt(name, items);
            }
            else
            {
                Serve(GraphCollections.Paths[name], items);
            }
        }
    }

    /// <summary>The server's base address, to use as the instance's <c>graph_url</c>.</summary>
    public Uri Url { get; }

    /// <summary>Plain HTTP clients; the collector sends absolute URLs under <see cref="Url"/>.</summary>
    public IHttpClientFactory HttpClients { get; } = new Clients();

    public IReadOnlyList<ServedRequest> Requests =>
        [.. _server.LogEntries.Select(e => new ServedRequest(e.RequestMessage?.Path ?? string.Empty, Status(e.ResponseMessage?.StatusCode)))];

    /// <summary>Starts serving <paramref name="tenant"/>, on <paramref name="port"/> or a free port.</summary>
    public static DevGraphServer Start(DevTenant tenant, int? port = null) =>
        new(port is { } fixedPort ? WireMockServer.Start(fixedPort) : WireMockServer.Start(), tenant);

    public void Dispose() => _server.Dispose();

    private void Serve(string path, JsonArray items)
    {
        var pages = items.Chunk(PageSize).ToArray();
        for (var i = 0; i < Math.Max(pages.Length, 1); i++)
        {
            var page = new JsonObject
            {
                ["@odata.context"] = $"{Url}{path.TrimStart('/').Split('/')[0]}/$metadata#{string.Join('/', path.Split('/').Skip(2))}",
                ["value"] = new JsonArray([.. (pages.Length == 0 ? [] : pages[i]).Select(item => item?.DeepClone())]),
            };
            if (i + 1 < pages.Length)
            {
                page["@odata.nextLink"] = string.Create(CultureInfo.InvariantCulture, $"{Url}{path.TrimStart('/')}?$skiptoken={i + 2}");
            }

            var request = Request.Create().WithPath(path).UsingGet();
            request = i == 0 ? request : request.WithParam("$skiptoken", (i + 1).ToString(CultureInfo.InvariantCulture));
            _server.Given(request).AtPriority(i == 0 ? 10 : 1)
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(page.ToJsonString()));
        }
    }

    /// <summary>Answers the advanced hunting query whose body carries <paramref name="name"/>'s marker, with its rows under <c>results</c>.</summary>
    private void Hunt(string name, JsonArray rows)
    {
        var columns = rows.OfType<JsonObject>().FirstOrDefault()?.Select(p => (JsonNode)new JsonObject { ["name"] = p.Key, ["type"] = p.Value is JsonValue v && v.TryGetValue<int>(out _) ? "Int64" : "String" }) ?? [];
        var answer = new JsonObject
        {
            ["@odata.context"] = $"{Url}v1.0/$metadata#microsoft.graph.security.huntingQueryResults",
            ["schema"] = new JsonArray([.. columns]),
            ["results"] = rows.DeepClone(),
        };
        _server.Given(Request.Create().WithPath(GraphCollections.HuntingPath).UsingPost()
                .WithBody((string? body) => body?.Contains($"evidence-hound: {name}", StringComparison.Ordinal) == true))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(answer.ToJsonString()));
    }

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
