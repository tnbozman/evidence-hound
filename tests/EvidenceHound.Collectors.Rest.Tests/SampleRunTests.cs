using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>
/// s09-23: a drafted profile's queries run once, read-only, against the real API; the answers are saved as a profile test's recordings with
/// identities hashed, tokens replaced and the API's address as <c>{{api}}</c>, so the test replays offline.
/// </summary>
public sealed class SampleRunTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private readonly RecordedExample _api = RecordedExample.Start();
    private readonly string _folder = Directory.CreateTempSubdirectory("hound-sample-run-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recordings_are_hashed_and_replay_offline()
    {
        var sample = (await Sample()).Value();

        var devices = sample.Responses.First(r => r.Path == "/v1/orgs/org-0001/devices");
        devices.Body.ShouldNotContain("laptop-01.example.com");
        devices.Body.ShouldNotContain("alex@example.com");
        devices.Body.ShouldContain(QueryEvidence.Hash("alex@example.com"));
        devices.Body.ShouldContain("\"version\": \"15.1\"");
        devices.Body.ShouldContain("{{api}}/v1/orgs/org-0001/devices?cursor=page-2");
        devices.Body.ShouldNotContain(_api.Url.Authority);
        var token = sample.Responses.Single(r => r.Path == "/oauth2/token");
        token.Body.ShouldNotContain(RecordedExample.Token);
        sample.Responses.Single(r => r.Query.GetValueOrDefault("cursor") == "page-2").Method.ShouldBe("GET");

        // Written as a profile test with the results the person confirmed, it replays offline and passes.
        File.Copy(Path.Combine(Profiles.LibraryFolder, "example.yaml"), Path.Combine(_folder, "example.yaml"));
        var expect = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["example.device.os_supported"] = "pass",
            ["example.device.recently_seen"] = "pass",
            ["example.identity.mfa_users"] = "pass",
            ["example.identity.owner_count"] = "pass",
        };
        SampleRun.WriteTestCase(sample, Path.Combine(_folder, "example.tests"), "sampled", new Dictionary<string, string>(StringComparer.Ordinal) { ["client_id"] = RecordedExample.ClientId, ["supported_os"] = "14, 15" }, ["client_secret"], Now, expect);

        var report = (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Value().ShouldHaveSingleItem();
        report.Unrecorded.ShouldBeEmpty();
        report.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckId}: {c.Actual} {c.Observation}").ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_folder, "example.tests", "sampled.yaml")).ShouldNotContain(RecordedExample.ClientSecret);
    }

    [Fact]
    public async Task A_failed_query_is_named_and_nothing_is_kept()
    {
        _api.Fail("/v1/orgs/org-0001/roles", 403);

        var problem = (await Sample()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("profile.sample_failed");
        problem.Message.ShouldContain("roles");
    }

    public void Dispose()
    {
        _api.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private Task<Core.Results.Result<SampleRecording>> Sample()
    {
        var settings = _api.Settings(_folder);
        var values = new InstanceValues(
            settings.Where(s => s.Key is not ("profile" or "profiles_path")).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal),
            new Dictionary<string, Secret>(StringComparer.Ordinal) { ["client_secret"] = new(RecordedExample.ClientSecret) },
            IncludeIdentities: false);
        return SampleRun.RunAsync(Profiles.Example, values, new SocketsHttpHandler { AllowAutoRedirect = false }, new TestClock(), Token);
    }
}
