using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class RestAuthTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    private readonly WireMockServer _other = WireMockServer.Start();

    public void Dispose()
    {
        _api.Dispose();
        _other.Dispose();
    }

    private static string OAuth => """{ type: oauth2_client_credentials, token_url: "${base_url}oauth2/token", client_id: example-client, client_secret: "${secrets.key}", scope: read }""";

    [Fact]
    public async Task Client_credentials_token_is_sent_only_to_allowed_hosts()
    {
        Token("token-one", 3600);
        Respond("/v1/whoami", new JsonObject { ["region"] = _other.Url });
        Respond("/v1/things", Items(1));
        _other.Given(Request.Create().UsingAnyMethod()).RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":[]}"""));
        var profile = Engines.Profile("""
              local:
                get: v1/things
                items: $.items
              regional:
                get: ${region}/v1/things
                items: $.items
            """, OAuth, extra: """
            bootstrap:
              whoami:
                get: v1/whoami
                vars: { region: $.region }
            """);

        var snapshot = await Engines.RunAsync(profile, _api);

        snapshot.Collected("local").Items.Count.ShouldBe(1);
        snapshot.Failed("regional").Reason.ShouldContain("not in allowed_hosts");
        _other.LogEntries.ShouldBeEmpty();
        var token = _api.LogEntries.Single(e => e.RequestMessage!.Path == "/oauth2/token").RequestMessage!;
        token.Body!.ShouldContain("grant_type=client_credentials");
        token.Body!.ShouldContain("client_id=example-client");
        token.Body!.ShouldContain("scope=read");
        _api.LogEntries.Where(e => e.RequestMessage!.Path == "/v1/things").ShouldAllBe(e => e.RequestMessage!.Headers!["Authorization"][0] == "Bearer token-one");
    }

    [Fact]
    public async Task Expired_token_is_refreshed()
    {
        Token("token-one", 60);
        Respond("/v1/first", Items(1));
        Respond("/v1/second", Items(1));
        var clock = new TestClock();
        var profile = Engines.Profile("""
              first:
                get: v1/first
                items: $.items
                vars: { first_done: "$.items[0].id" }
              second:
                get: v1/second?after=${first_done}
                items: $.items
            """, OAuth);
        _api.Given(Request.Create().WithPath("/v1/first")).AtPriority(1).RespondWith(Response.Create().WithCallback(_ =>
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            return new WireMock.ResponseMessage { StatusCode = 200, BodyData = new WireMock.Util.BodyData { BodyAsString = Items(1).ToJsonString(), DetectedBodyType = WireMock.Types.BodyType.String } };
        }));

        var snapshot = await Engines.RunAsync(profile, _api, clock);

        snapshot.Collected("second").Items.Count.ShouldBe(1);
        _api.LogEntries.Count(e => e.RequestMessage!.Path == "/oauth2/token").ShouldBe(2);
    }

    [Fact]
    public async Task Refused_token_fails_every_query_without_calling_them()
    {
        _api.Given(Request.Create().WithPath("/oauth2/token")).RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"error":"invalid_client"}"""));
        Respond("/v1/things", Items(1));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              things:
                get: v1/things
                items: $.items
            """, OAuth), _api);

        snapshot.Failed("things").Reason.ShouldContain("token");
        _api.LogEntries.ShouldNotContain(e => e.RequestMessage!.Path == "/v1/things");
    }

    [Fact]
    public async Task Api_key_in_a_query_parameter_is_sent()
    {
        Respond("/v1/things", Items(2));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              things:
                get: v1/things
                items: $.items
            """, """{ type: api_key, in: query, name: apikey, value: "${secrets.key}" }"""), _api);

        snapshot.Collected("things").Items.Count.ShouldBe(2);
        _api.LogEntries.Single().RequestMessage!.Query!["apikey"][0].ShouldBe("engine-test-key-91be44");
    }

    [Fact]
    public async Task Basic_auth_is_sent()
    {
        Respond("/v1/things", Items(1));

        await Engines.RunAsync(Engines.Profile("""
              things:
                get: v1/things
                items: $.items
            """, """{ type: basic, username: reader, password: "${secrets.key}" }"""), _api);

        var header = _api.LogEntries.Single().RequestMessage!.Headers!["Authorization"][0];
        header.ShouldBe("Basic " + Convert.ToBase64String("reader:engine-test-key-91be44"u8.ToArray()));
    }

    [Fact]
    public async Task Redirect_to_another_host_is_refused()
    {
        _api.Given(Request.Create().WithPath("/v1/things")).RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", _other.Url + "/v1/things"));

        var snapshot = await Engines.RunAsync(Engines.Profile("""
              things:
                get: v1/things
                items: $.items
            """), _api);

        snapshot.Failed("things").Reason.ShouldContain("redirect");
        _other.LogEntries.ShouldBeEmpty();
    }

    private void Token(string token, int expiresIn) =>
        _api.Given(Request.Create().WithPath("/oauth2/token").UsingPost()).RespondWith(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json").WithBody(new JsonObject { ["access_token"] = token, ["expires_in"] = expiresIn }.ToJsonString()));

    private void Respond(string path, JsonNode body) =>
        _api.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(10).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body.ToJsonString()));

    internal static JsonObject Items(int count) => new() { ["items"] = new JsonArray([.. Enumerable.Range(1, count).Select(i => (JsonNode)new JsonObject { ["id"] = $"item-{i}" })]) };
}
