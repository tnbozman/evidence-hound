using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>Advanced hunting through Graph: one POST with the query and its time span, answered with rows under <c>results</c>.</summary>
public sealed class HuntingQueryTests : IDisposable
{
    private readonly RecordedApi _graph = RecordedApi.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Hunting_sources_carry_their_query_window_and_permission()
    {
        var sources = GraphSources.Hunting(30);

        sources.Select(s => s.Name).ShouldBe(["hunting-app-control-events", "hunting-app-control-blocked-files", "hunting-defender-onboarding"]);
        sources.ShouldAllBe(s => s.Request.Path == "v1.0/security/runHuntingQuery" && s.Request.Permission == "ThreatHunting.Read.All" && s.MayBeRefused);
        sources.ShouldAllBe(s => s.Request.Body!["Timespan"]!.GetValue<string>() == "P30D");
        sources.ShouldAllBe(s => s.Request.Body!["Query"]!.GetValue<string>().Contains($"// evidence-hound: {s.Name}", StringComparison.Ordinal));
        sources[0].Request.Body!["Query"]!.GetValue<string>().ShouldContain("DeviceEvents");
        sources[2].Request.Body!["Query"]!.GetValue<string>().ShouldContain("DeviceInfo");
    }

    [Fact]
    public async Task Sends_query_and_timespan()
    {
        _graph.Respond("POST", "/v1.0/security/runHuntingQuery", 200, """
            { "schema": [{ "name": "Devices", "type": "Int64" }], "results": [{ "OnboardingStatus": "Onboarded", "Devices": 5 }] }
            """);
        var source = GraphSources.Hunting(30)[2];

        var collection = (await Client().GetAllAsync(source.Request, Token)).Value();

        collection.Items.ShouldHaveSingleItem()!["Devices"]!.GetValue<int>().ShouldBe(5);
        collection.Pages.ShouldBe(1);
        var sent = _graph.Server.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        sent.Method.ShouldBe("POST");
        var body = JsonNode.Parse(sent.Body!)!;
        body["Query"]!.GetValue<string>().ShouldBe(source.Request.Body!["Query"]!.GetValue<string>());
        body["Timespan"]!.GetValue<string>().ShouldBe("P30D");
    }

    public void Dispose() => _graph.Dispose();

    private GraphClient Client() => new(_graph.HttpClients.CreateClient("m365"), TestCredentials.Working, _graph.Url, new RecordingWait());
}
