using EvidenceHound.Collectors.Rest.Engine;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class RestPagingTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Follows_next_link_until_absent()
    {
        Page(Request.Create().WithPath("/v1/things"), new JsonObject { ["items"] = Ids(1, 2), ["next"] = "v1/things?skiptoken=b" }, priority: 10);
        Page(Request.Create().WithPath("/v1/things").WithParam("skiptoken", "b"), new JsonObject { ["items"] = Ids(3), ["next"] = _api.Url + "/v1/things?skiptoken=c" });
        Page(Request.Create().WithPath("/v1/things").WithParam("skiptoken", "c"), new JsonObject { ["items"] = Ids(4) });

        var snapshot = await Run("paging: { style: next_link, next: $.next }");

        var things = snapshot.Collected("things");
        Ids(things).ShouldBe(["1", "2", "3", "4"]);
        things.Pages.ShouldBe(3);
    }

    [Fact]
    public async Task Follows_page_numbers_up_to_the_total()
    {
        Page(Request.Create().WithPath("/v1/things").WithParam("page", "1").WithParam("pageSize", "2"), new JsonObject { ["items"] = Ids(1, 2), ["pages"] = new JsonObject { ["total"] = 2 } });
        Page(Request.Create().WithPath("/v1/things").WithParam("page", "2").WithParam("pageSize", "2"), new JsonObject { ["items"] = Ids(3), ["pages"] = new JsonObject { ["total"] = 2 } });

        var snapshot = await Run("paging: { style: page_number, page: page, size_param: pageSize, size: 2, total_pages: $.pages.total }");

        Ids(snapshot.Collected("things")).ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public async Task Follows_cursor_until_none()
    {
        Page(Request.Create().WithPath("/v1/things"), new JsonObject { ["items"] = Ids(1), ["meta"] = new JsonObject { ["cursor"] = "abc" } }, priority: 10);
        Page(Request.Create().WithPath("/v1/things").WithParam("cursor", "abc"), new JsonObject { ["items"] = Ids(2), ["meta"] = new JsonObject { ["cursor"] = null } });

        var snapshot = await Run("paging: { style: cursor, cursor: $.meta.cursor, param: cursor }");

        Ids(snapshot.Collected("things")).ShouldBe(["1", "2"]);
    }

    [Fact]
    public async Task Follows_offset_and_limit_up_to_the_total()
    {
        Page(Request.Create().WithPath("/v1/things").WithParam("offset", "0").WithParam("limit", "2"), new JsonObject { ["items"] = Ids(1, 2), ["total"] = 3 });
        Page(Request.Create().WithPath("/v1/things").WithParam("offset", "2").WithParam("limit", "2"), new JsonObject { ["items"] = Ids(3), ["total"] = 3 });

        var snapshot = await Run("paging: { style: offset, offset_param: offset, limit_param: limit, limit: 2, total: $.total }");

        Ids(snapshot.Collected("things")).ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public async Task Offset_without_total_stops_at_a_short_page()
    {
        Page(Request.Create().WithPath("/v1/things").WithParam("offset", "0"), new JsonObject { ["items"] = Ids(1, 2) });
        Page(Request.Create().WithPath("/v1/things").WithParam("offset", "2"), new JsonObject { ["items"] = Ids(3) });

        var snapshot = await Run("paging: { style: offset, limit: 2 }");

        Ids(snapshot.Collected("things")).ShouldBe(["1", "2", "3"]);
        _api.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Follows_the_link_header()
    {
        _api.Given(Request.Create().WithPath("/v1/things")).AtPriority(10).RespondWith(Response.Create().WithStatusCode(200)
            .WithHeader("Link", $"<{_api.Url}/v1/things?page=2>; rel=\"next\", <{_api.Url}/v1/things?page=9>; rel=\"last\"").WithBody(new JsonArray(Ids(1)).ToJsonString()));
        _api.Given(Request.Create().WithPath("/v1/things").WithParam("page", "2")).RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonArray(Ids(2)).ToJsonString()));

        var snapshot = await Run("paging: { style: link_header }", items: "$");

        Ids(snapshot.Collected("things")).ShouldBe(["1", "2"]);
    }

    [Fact]
    public async Task Stops_at_page_cap_and_reports_it()
    {
        Page(Request.Create().WithPath("/v1/things"), new JsonObject { ["items"] = Ids(1), ["next"] = "v1/things?again=1" });

        var snapshot = await Run("paging: { style: next_link, next: $.next }\n    max_pages: 3");

        var failed = snapshot.Failed("things");
        failed.Reason.ShouldContain("page cap of 3");
        _api.LogEntries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Item_cap_is_reported()
    {
        Page(Request.Create().WithPath("/v1/things"), new JsonObject { ["items"] = Ids(1, 2, 3) });

        var snapshot = await Run("max_items: 2");

        snapshot.Failed("things").Reason.ShouldContain("item cap of 2");
    }

    [Fact]
    public async Task Keep_holds_only_listed_fields()
    {
        Page(Request.Create().WithPath("/v1/things"), new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = "1", ["name"] = "a", ["big"] = "blob" }) });

        var snapshot = await Run("keep: [id, name]");

        snapshot.Collected("things").Items[0]!.AsObject().Select(p => p.Key).ShouldBe(["id", "name"]);
    }

    private Task<Snapshot> Run(string extra, string items = "$.items") =>
        Engines.RunAsync(Engines.Profile($"""
              things:
                get: v1/things
                items: {items}
                {extra}
            """), _api);

    private void Page(IRequestBuilder request, JsonObject body, int priority = 1) =>
        _api.Given(request.UsingGet()).AtPriority(priority).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body.ToJsonString()));

    private static JsonArray Ids(params int[] ids) => [.. ids.Select(i => (JsonNode)new JsonObject { ["id"] = $"{i}" })];

    private static string[] Ids(QueryResult.Collected collected) => [.. collected.Items.Select(i => i!["id"]!.GetValue<string>())];
}
