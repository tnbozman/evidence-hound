using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Secrets;
using WireMock.Server;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>Runs the query engine on a small profile against a WireMock server, for focused engine tests.</summary>
internal static class Engines
{
    /// <summary>A profile reading the test server: <paramref name="queries"/> is the YAML under <c>queries:</c>, indented two spaces.</summary>
    public static Profile Profile(string queries, string auth = "{ type: bearer, token: \"${secrets.token}\" }", string extra = "") =>
        ProfileParser.Parse($$"""
            profile: engine-test
            version: "1.0.0"
            parameters:
              base_url: { type: url, required: true }
            secrets:
              token: { required: false }
              key: { required: false }
            connection:
              base_url: ${base_url}
              allowed_hosts: ["${base_url}"]
              auth: {{auth}}
              rate_limit: { per_second: 1000, max_retries: 2 }
            {{extra}}
            queries:
            {{queries}}
            """, "engine-test.yaml").Value();

    public static InstanceValues Values(WireMockServer server, bool includeIdentities = false) => new(
        new Dictionary<string, string>(StringComparer.Ordinal) { ["base_url"] = server.Url + "/" },
        new Dictionary<string, Secret>(StringComparer.Ordinal) { ["token"] = new("engine-test-token-3a7f0c"), ["key"] = new("engine-test-key-91be44") },
        includeIdentities);

    public static Task<Snapshot> RunAsync(Profile profile, WireMockServer server, TestClock? clock = null)
    {
        clock ??= new TestClock();
        var engine = new QueryEngine(RecordedExample.HttpClients.CreateClient("rest"), clock, clock);
        return engine.CollectAsync(profile, Values(server), TestContext.Current.CancellationToken);
    }

    public static QueryResult.Collected Collected(this Snapshot snapshot, string name) =>
        snapshot.Queries[name].ShouldBeOfType<QueryResult.Collected>();

    public static QueryResult.Failed Failed(this Snapshot snapshot, string name) =>
        snapshot.Queries[name].ShouldBeOfType<QueryResult.Failed>();
}
