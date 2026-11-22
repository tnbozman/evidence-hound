using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class RestRateLimitTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    public void Dispose() => _api.Dispose();

    private static string Things => """
          things:
            get: v1/things
            items: $.items
        """;

    [Fact]
    public async Task Honours_retry_after_then_gives_up()
    {
        _api.Given(Request.Create().WithPath("/v1/things")).RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "7"));
        var clock = new TestClock();

        var snapshot = await Engines.RunAsync(Engines.Profile(Things), _api, clock);

        snapshot.Failed("things").Reason.ShouldContain("429");
        _api.LogEntries.Count.ShouldBe(3, "the first try and max_retries (2) retries");
        clock.Waits.Count(w => w == TimeSpan.FromSeconds(7)).ShouldBe(2);
    }

    [Fact]
    public async Task Service_unavailable_is_retried_then_succeeds()
    {
        _api.Given(Request.Create().WithPath("/v1/things")).InScenario("busy").WillSetStateTo("ready")
            .RespondWith(Response.Create().WithStatusCode(503));
        _api.Given(Request.Create().WithPath("/v1/things")).InScenario("busy").WhenStateIs("ready")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(RestAuthTests.Items(2).ToJsonString()));

        var snapshot = await Engines.RunAsync(Engines.Profile(Things), _api);

        snapshot.Collected("things").Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_rate_limit()
    {
        _api.Given(Request.Create().WithPath("/v1/things")).RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["items"] = new JsonArray(), ["next"] = "v1/things?p=2" }.ToJsonString()));
        _api.Given(Request.Create().WithPath("/v1/things").WithParam("p", "2")).AtPriority(1).RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":[]}"""));
        var clock = new TestClock();
        var profile = Engines.Profile(Things.Replace("items: $.items", "items: $.items\n    paging: { style: next_link, next: $.next }", StringComparison.Ordinal))
            with { Connection = Engines.Profile(Things).Connection with { RateLimit = new EvidenceHound.Collectors.Rest.Profiles.RateLimit(2, 2) } };

        await Engines.RunAsync(profile, _api, clock);

        clock.Waits.ShouldBe([TimeSpan.FromMilliseconds(500)]);
    }
}
