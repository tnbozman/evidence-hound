using EvidenceHound.Collectors.Rest.Engine;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class QueryGraphTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Bootstrap_variables_feed_later_requests()
    {
        Respond("/v1/whoami", new JsonObject { ["tenant"] = new JsonObject { ["id"] = "tenant-7" } });
        Respond("/v1/tenants/tenant-7/things", RestAuthTests.Items(2), header: ("X-Tenant", "tenant-7"));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              things:
                get: v1/tenants/${tenant_id}/things
                headers: { X-Tenant: "${tenant_id}" }
                items: $.items
            """, extra: """
            bootstrap:
              whoami:
                get: v1/whoami
                vars: { tenant_id: $.tenant.id }
            """), _api);

        snapshot.Collected("things").Items.Count.ShouldBe(2);
        snapshot.Vars["tenant_id"].ShouldBe("tenant-7");
        snapshot.Queries["whoami"].ShouldBeOfType<QueryResult.Collected>().IsBootstrap.ShouldBeTrue();
    }

    [Fact]
    public async Task For_each_calls_once_per_item_and_merges_the_items()
    {
        Respond("/v1/firewalls", new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = "fw-1" }, new JsonObject { ["id"] = "fw-2" }) });
        _api.Given(Request.Create().WithPath("/v1/upgrades").UsingPost()).RespondWith(Response.Create().WithCallback(request => new WireMock.ResponseMessage
        {
            StatusCode = 200,
            BodyData = new WireMock.Util.BodyData
            {
                BodyAsString = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["firewall"] = JsonNode.Parse(request.Body!)!["firewalls"]![0]!.GetValue<string>(), ["upgrade"] = "21.5" }) }.ToJsonString(),
                DetectedBodyType = WireMock.Types.BodyType.String,
            },
        }));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              firewalls:
                get: v1/firewalls
                items: $.items
              upgrades:
                for_each: firewalls
                post: v1/upgrades
                body: { firewalls: ["${item.id}"] }
                items: $.items
            """), _api);

        var upgrades = snapshot.Collected("upgrades");
        upgrades.Items.Select(i => i!["firewall"]!.GetValue<string>()).ShouldBe(["fw-1", "fw-2"]);
        upgrades.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task For_each_is_capped_by_max_calls()
    {
        Respond("/v1/firewalls", RestAuthTests.Items(3));
        Respond("/v1/firewalls/item-1/detail", RestAuthTests.Items(1));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              firewalls:
                get: v1/firewalls
                items: $.items
              detail:
                for_each: firewalls
                max_calls: 2
                get: v1/firewalls/${item.id}/detail
                items: $.items
            """), _api);

        snapshot.Failed("detail").Reason.ShouldContain("max_calls of 2");
        _api.LogEntries.ShouldNotContain(e => e.RequestMessage!.Path.EndsWith("/detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_query_depending_on_a_failed_one_fails_naming_it()
    {
        _api.Given(Request.Create().WithPath("/v1/firewalls")).RespondWith(Response.Create().WithStatusCode(500));
        Respond("/v1/others", RestAuthTests.Items(1));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              firewalls:
                get: v1/firewalls
                items: $.items
              detail:
                for_each: firewalls
                get: v1/firewalls/${item.id}
              others:
                get: v1/others
                items: $.items
            """), _api);

        snapshot.Failed("firewalls").Reason.ShouldContain("500");
        snapshot.Failed("detail").Reason.ShouldContain("'firewalls'");
        snapshot.Collected("others").Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Optional_status_is_unavailable_not_failed()
    {
        _api.Given(Request.Create().WithPath("/v1/premium")).RespondWith(Response.Create().WithStatusCode(403).WithBody("""{"error":"licence required"}"""));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              premium:
                get: v1/premium
                optional_on: [403, 404]
            """), _api);

        snapshot.Queries["premium"].ShouldBeOfType<QueryResult.Unavailable>().Reason.ShouldContain("403");
    }

    private void Respond(string path, JsonNode body, (string Name, string Value)? header = null)
    {
        var request = Request.Create().WithPath(path).UsingGet();
        if (header is { } h)
        {
            request = request.WithHeader(h.Name, h.Value);
        }

        _api.Given(request).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body.ToJsonString()));
    }
}
