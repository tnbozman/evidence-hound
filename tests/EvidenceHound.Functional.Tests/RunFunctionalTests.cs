using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class RunFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-run-functional-").FullName;

    [Fact]
    public async Task Run_with_fake_collector_updates_example_audit()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exitCode, summary) = await RunAsync(ct);

        exitCode.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        var publish = summary["collectors"]![0]!["publish"]!;
        (publish["failed"]!.GetValue<int>(), publish["planned"]!.GetValue<int>()).ShouldBe((0, 0));
        var mfa = await RequirementAsync("EX-1.1", ct);
        mfa["result"]!.GetValue<string>().ShouldBe("non_compliant");
        mfa["observation"]!.GetValue<string>().ShouldContain("3 of 40 user accounts have no MFA method.");
        mfa["evidences"]!.AsArray().Count.ShouldBe(2);
        (await RequirementAsync("EX-3.1", ct))["result"]!.GetValue<string>().ShouldBe("compliant");
        var evidence = (await GetAsync("evidences/?search=example-fake", ct))["results"]!.AsArray()
            .Where(e => e!["ref_id"]!.GetValue<string>() == "example-fake/mfa-users.json").ToList();
        evidence.ShouldHaveSingleItem();

        var health = await HealthEvidenceAsync(ct);
        var control = (await GetAsync("applied-controls/?ref_id=AC-AUTO-EVIDENCE", ct))["results"]![0]!;
        var linked = (await GetAsync($"applied-controls/{control["id"]!.GetValue<string>()}/", ct))["evidences"]!.AsArray()
            .Select(e => e is JsonObject o ? o["id"]!.GetValue<string>() : e!.GetValue<string>());
        linked.ShouldContain(health["id"]!.GetValue<string>());
        var revisionsAfterFirst = await RevisionCountAsync(health, ct);

        var (secondExit, second) = await RunAsync(ct);

        secondExit.ShouldBe(ExitCodes.Ok, second.ToJsonString());
        // s06-04: nothing changed, so nothing is planned: evidence with a known hash is not even looked up again.
        (second["collectors"]![0]!["publish"]!["sent"]!.GetValue<int>(), second["collectors"]![0]!["publish"]!["skipped"]!.GetValue<int>()).ShouldBe((0, 0));
        (await RevisionCountAsync(health, ct)).ShouldBe(revisionsAfterFirst + 1);
    }

    private async Task<JsonNode> HealthEvidenceAsync(CancellationToken ct) =>
        (await GetAsync("evidences/?search=hound-health", ct))["results"]!.AsArray()
            .Single(e => e!["ref_id"]!.GetValue<string>() == "hound-health/example-fake")!;

    private async Task<int> RevisionCountAsync(JsonNode evidence, CancellationToken ct) =>
        (await GetAsync($"evidence-revisions/?evidence={evidence["id"]!.GetValue<string>()}", ct))["results"]!.AsArray().Count;

    private async Task<(int ExitCode, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var config = Path.Combine(_workDir, "hound.yaml");
        var example = await File.ReadAllTextAsync(Path.Combine(files, "examples", "example-pty-ltd.yaml"), ct);
        await File.WriteAllTextAsync(config, example
            .Replace("root: ../.dev/evidence", $"root: {Path.Combine(_workDir, "evidence")}", StringComparison.Ordinal)
            .Replace("mapping: ../mappings/example.yaml", $"mapping: {Path.Combine(files, "mappings", "example.yaml")}", StringComparison.Ordinal), ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["run", "--config", config],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        var text = output.ToString();
        return (exitCode, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    private async Task<JsonNode> RequirementAsync(string requirement, CancellationToken ct) =>
        (await GetAsync($"requirement-assessments/?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id={requirement}", ct))["results"]![0]!;

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);
}
