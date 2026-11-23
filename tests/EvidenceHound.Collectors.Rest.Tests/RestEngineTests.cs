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

    public void Dispose() => _api.Dispose();

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
