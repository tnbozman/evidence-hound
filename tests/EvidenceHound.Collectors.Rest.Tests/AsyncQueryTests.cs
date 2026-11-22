using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class AsyncQueryTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    public void Dispose() => _api.Dispose();

    private static string Report => """
          report:
            post: v1/reports
            body: { kind: devices }
            async: { status_url: $.statusUrl, done: { field: $.status, equals: done }, result_url: $.resultUrl, poll_seconds: 5, timeout_seconds: 60 }
            items: $.rows
        """;

    [Fact]
    public async Task Polls_until_done_then_downloads()
    {
        _api.Given(Request.Create().WithPath("/v1/reports").UsingPost()).RespondWith(Json(new JsonObject { ["statusUrl"] = "v1/reports/r1" }, 202));
        _api.Given(Request.Create().WithPath("/v1/reports/r1").UsingGet()).InScenario("job").WillSetStateTo("done").RespondWith(Json(new JsonObject { ["status"] = "running" }));
        _api.Given(Request.Create().WithPath("/v1/reports/r1").UsingGet()).InScenario("job").WhenStateIs("done")
            .RespondWith(Json(new JsonObject { ["status"] = "done", ["resultUrl"] = _api.Url + "/v1/reports/r1/result" }));
        _api.Given(Request.Create().WithPath("/v1/reports/r1/result").UsingGet()).RespondWith(Json(new JsonObject { ["rows"] = new JsonArray(new JsonObject { ["id"] = "a" }) }));
        var clock = new TestClock();

        var snapshot = await Engines.RunAsync(Engines.Profile(Report), _api, clock);

        snapshot.Collected("report").Items.Count.ShouldBe(1);
        clock.Waits.Count(w => w == TimeSpan.FromSeconds(5)).ShouldBe(2);
    }

    [Fact]
    public async Task A_job_that_never_finishes_times_out()
    {
        _api.Given(Request.Create().WithPath("/v1/reports").UsingPost()).RespondWith(Json(new JsonObject { ["statusUrl"] = "v1/reports/r1" }, 202));
        _api.Given(Request.Create().WithPath("/v1/reports/r1").UsingGet()).RespondWith(Json(new JsonObject { ["status"] = "running" }));

        var snapshot = await Engines.RunAsync(Engines.Profile(Report), _api);

        snapshot.Failed("report").Reason.ShouldContain("did not finish within 60 seconds");
    }

    private static IResponseBuilder Json(JsonNode body, int status = 200) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body.ToJsonString());
}
