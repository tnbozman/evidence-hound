using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class PublishFunctionalTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task Publish_example_plan_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;

        var (firstExit, first) = await PublishAsync(ct);
        var (secondExit, second) = await PublishAsync(ct);

        firstExit.ShouldBe(ExitCodes.Ok, first.ToJsonString());
        (first["sent"]!.GetValue<int>(), first["failed"]!.GetValue<int>()).ShouldBe((10, 0));
        secondExit.ShouldBe(ExitCodes.Ok, second.ToJsonString());
        (second["skipped"]!.GetValue<int>(), second["sent"]!.GetValue<int>(), second["failed"]!.GetValue<int>()).ShouldBe((3, 7, 0));

        var evidence = (await Get("evidences/?search=ev-0001", ct))["results"]!.AsArray()
            .Where(e => e!["ref_id"]!.GetValue<string>() == "ev-0001").ToList();
        var only = evidence.ShouldHaveSingleItem()!;
        var revisions = await Get($"evidence-revisions/?evidence={only["id"]!.GetValue<string>()}", ct);
        revisions["results"]!.AsArray().Count.ShouldBe(1);

        var requirement = (await Get("requirement-assessments/?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id=EX-1.1", ct))["results"]![0]!;
        requirement["result"]!.GetValue<string>().ShouldBe("non_compliant");
        var control = (await Get("applied-controls/?ref_id=AC-BACKUP", ct))["results"]![0]!;
        control["status"]!.GetValue<string>().ShouldBe("active");
    }

    private async Task<(int ExitCode, JsonNode Report)> PublishAsync(CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var exitCode = await HoundCli.RunAsync(
            [
                "publish",
                "--config", Path.Combine(files, "examples", "example-pty-ltd.yaml"),
                "--checks", Path.Combine(files, "fixtures", "publish", "checks.json"),
                "--evidence-dir", Path.Combine(files, "fixtures", "publish", "evidence"),
            ],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        var text = output.ToString();
        return (exitCode, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    private async Task<JsonNode> Get(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
