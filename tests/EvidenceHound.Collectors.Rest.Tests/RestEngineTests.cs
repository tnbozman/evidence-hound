using System.IO.Compression;
using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>s09-33: engine extensions for reading a pipeline's artefact.</summary>
public sealed class RestEngineTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    private readonly WireMockServer _storage = WireMockServer.Start();

    public void Dispose()
    {
        _api.Dispose();
        _storage.Dispose();
    }

    [Fact]
    public async Task Redirect_to_a_listed_host_is_followed_without_credentials()
    {
        _api.Given(Request.Create().WithPath("/v1/artefact.zip").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", _storage.Url + "/signed/artefact.zip?sig=abc"));
        _storage.Given(Request.Create().WithPath("/signed/artefact.zip").UsingGet())
            .RespondWith(Response.Create().WithBody(Zip(("plan.json", "{\"resource_drift\": []}"))));
        var storage = new Uri(_storage.Url!).Authority;

        var snapshot = await Engines.RunAsync(Engines.Profile($"""
              listed:
                get: v1/artefact.zip
                format: zip
                member: plan.json
                redirect_hosts: ["{storage}"]
              unlisted:
                get: v1/artefact.zip
                format: zip
                member: plan.json
            """), _api);

        snapshot.Collected("listed").Items.Count.ShouldBe(1);
        snapshot.Failed("unlisted").Reason.ShouldContain("redirect");
        var download = _storage.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        download.Headers!.Keys.ShouldNotContain("Authorization");
        download.RawQuery.ShouldContain("sig=abc");
    }

    private static string Trigger => """
          dispatch:
            post: repos/example/grc-infra/actions/workflows/drift.yml/dispatches
            body: { ref: main, return_run_details: true }
            write: true
            async: { status_url: $.run_url, done: { field: $.status, equals: completed }, result_url: $.artifacts_url, poll_seconds: 10, timeout_seconds: 600 }
            items: $.artifacts
        """;

    [Fact]
    public async Task Trigger_is_refused_unless_both_profile_and_settings_opt_in()
    {
        _api.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(500));

        var off = await Engines.RunAsync(Engines.Profile(Trigger), _api);

        off.Failed("dispatch").Reason.ShouldContain("allow_writes");
        _api.LogEntries.ShouldBeEmpty();
        Engines.Profile(Trigger).Queries.Single().Write.ShouldBeTrue();
        Engines.Profile("  runs:\n    get: v1/runs\n").Queries.Single().Write.ShouldBeFalse();
    }

    [Fact]
    public async Task Trigger_polls_the_run_then_reads_its_artefact()
    {
        var run = _api.Url + "/repos/example/grc-infra/actions/runs/7001";
        _api.Given(Request.Create().WithPath("/repos/example/grc-infra/actions/workflows/drift.yml/dispatches").UsingPost())
            .RespondWith(Json($$"""{"workflow_run_id": 7001, "run_url": "{{run}}"}"""));
        _api.Given(Request.Create().WithPath("/repos/example/grc-infra/actions/runs/7001").UsingGet()).InScenario("run").WillSetStateTo("completed")
            .RespondWith(Json("""{"id": 7001, "status": "in_progress"}"""));
        _api.Given(Request.Create().WithPath("/repos/example/grc-infra/actions/runs/7001").UsingGet()).InScenario("run").WhenStateIs("completed")
            .RespondWith(Json($$"""{"id": 7001, "status": "completed", "conclusion": "success", "artifacts_url": "{{run}}/artifacts"}"""));
        _api.Given(Request.Create().WithPath("/repos/example/grc-infra/actions/runs/7001/artifacts").UsingGet())
            .RespondWith(Json("""{"total_count": 1, "artifacts": [{"id": 9001, "name": "drift-plan", "workflow_run": {"id": 7001, "head_sha": "0123abc"}}]}"""));
        var clock = new TestClock();

        var snapshot = await Engines.RunAsync(Engines.Profile(Trigger), _api, clock, allowWrites: true);

        snapshot.Collected("dispatch").Items.Single()!["id"]!.GetValue<int>().ShouldBe(9001);
        _api.LogEntries.Count(e => e.RequestMessage!.Method == "POST").ShouldBe(1);
        clock.Waits.Count(w => w == TimeSpan.FromSeconds(10)).ShouldBe(2);
    }

    private static IResponseBuilder Json(string body) => Response.Create().WithHeader("Content-Type", "application/json").WithBody(body);

    [Fact]
    public async Task Zip_member_is_read_with_the_size_caps()
    {
        var plan = """{"format_version":"1.2","resource_drift":[{"address":"example_instance.grc","change":{"actions":["update"]}}]}""";
        _api.Given(Request.Create().WithPath("/v1/artefact.zip").UsingGet())
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/zip").WithBody(Zip(("plan.json", plan), ("readme.txt", "not this one"))));
        static string Query(string name, string member, int maxBytes) => $"""
              {name}:
                get: v1/artefact.zip
                format: zip
                member: {member}
                items: $.resource_drift
                max_bytes: {maxBytes}
            """;

        var snapshot = await Engines.RunAsync(
            Engines.Profile(Query("plan", "plan.json", 10_000) + "\n" + Query("missing", "other.json", 10_000) + "\n" + Query("capped", "plan.json", 60)), _api);

        snapshot.Collected("plan").Items.Single()!["address"]!.GetValue<string>().ShouldBe("example_instance.grc");
        snapshot.Failed("missing").Reason.ShouldContain("other.json");
        snapshot.Failed("capped").Reason.ShouldContain("byte cap of 60 bytes");
    }

    [Fact]
    public async Task A_member_that_unzips_past_the_cap_is_refused()
    {
        var padded = "{\"resource_drift\": []" + new string(' ', 200_000) + "}";
        _api.Given(Request.Create().WithPath("/v1/artefact.zip").UsingGet()).RespondWith(Response.Create().WithBody(Zip(("plan.json", padded))));

        var snapshot = await Engines.RunAsync(Engines.Profile("  plan:\n    get: v1/artefact.zip\n    format: zip\n    member: plan.json\n    max_bytes: 100000\n"), _api);

        snapshot.Failed("plan").Reason.ShouldContain("byte cap of 100000 bytes once unzipped");
    }

    [Fact]
    public void Member_is_required_with_zip_and_only_with_zip()
    {
        Should.Throw<ShouldAssertException>(() => Engines.Profile("  plan:\n    get: v1/a.zip\n    format: zip\n")).Message.ShouldContain("member");
        Should.Throw<ShouldAssertException>(() => Engines.Profile("  plan:\n    get: v1/a.json\n    member: plan.json\n")).Message.ShouldContain("member");
    }

    private static byte[] Zip(params (string Name, string Text)[] members)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in members)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return stream.ToArray();
    }
}
