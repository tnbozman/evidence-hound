using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s11-09, s11-12 to s11-15: the example scope's AI6 programme on CISO Assistant v4.3.4. The seed loads the AI6 library and writes the
/// AI6 audit and the two example AI systems with their labels; a run of the ai-register collector and the ciso-assistant profile then
/// fills the AI6 rows they cover.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class AiGovernanceFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-ai-functional-").FullName;

    [Fact]
    public async Task Seed_registers_the_ai_systems_with_their_labels_and_the_foundations_audit()
    {
        var ct = TestContext.Current.CancellationToken;

        var assets = (await GetAsync("assets/?limit=500", ct))["results"]!.AsArray().ToDictionary(a => a!["name"]!.GetValue<string>());
        var triage = assets["Example ticket triage"]!;
        Labels(triage).ShouldBe(["ai-oversight-in-the-loop", "ai-system", "ai-tier-low", "ai-use-built"], ignoreOrder: true, triage.ToJsonString());
        triage["description"]!.GetValue<string>().ShouldContain("AI register: low tier, built, human in the loop.");
        Labels(assets["Example proposal assistant"]!).ShouldContain("ai-tier-medium");
        Labels(assets["Example 365"]!).ShouldNotContain("ai-system");

        var audit = (await GetAsync("compliance-assessments/?ref_id=EX-AI6-2026", ct))["results"]!.AsArray().ShouldHaveSingleItem()!;
        await WriteResultAsync("ai6-audit.json", audit, ct);
        (await RowsAsync("AI6-5.1", ct)).Count.ShouldBe(1);
        (await RowsAsync("AI6-4.4", ct)).ShouldBeEmpty("AI6-4.4 is Implementation only, so a Foundations audit leaves it out");
    }

    [Fact]
    public async Task A_run_fills_the_ai6_rows_from_the_register_and_ciso_assistant()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exit, summary) = await RunAsync(ct);
        await WriteResultAsync("ai6-run.json", summary, ct);

        exit.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        var register = await RowAsync("AI6-4.1", ct);
        register["result"]!.GetValue<string>().ShouldBe("compliant", register.ToJsonString());
        var tested = await RowAsync("AI6-5.1", ct);
        tested["result"]!.GetValue<string>().ShouldBe("non_compliant", tested.ToJsonString());
        tested["observation"]!.GetValue<string>().ShouldContain("Example proposal assistant");
        var risks = await RowAsync("AI6-3.3", ct);
        risks["result"]!.GetValue<string>().ShouldBe("non_compliant", risks.ToJsonString());
        risks["observation"]!.GetValue<string>().ShouldContain("0 of 1 medium or high tier AI systems are linked to a risk scenario");
    }

    [Fact]
    public async Task Programme_list_shows_the_ai_register()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["programme", "list", "--scope", CisoAssistantFixture.ExampleScope], output, error, configureLogging: null, TestContext.Current.CancellationToken);

        exit.ShouldBe(ExitCodes.Ok, error.ToString());
        output.ToString().ShouldContain("Register: 2 AI systems. tier: low 1, medium 1. use: built 1, customised 1.");
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    private async Task<(int Exit, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var token = Path.Combine(_workDir, "ca_read_token");
        await File.WriteAllTextAsync(token, ciso.Token, ct);
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $$"""
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {{Path.Combine(_workDir, "evidence")}}
            collectors:
              - name: example-ai-register
                type: ai-register
                schedule: "0 6 * * *"
                settings:
                  scope: {{CisoAssistantFixture.ExampleScope}}
              - name: example-ciso-assistant
                type: rest
                schedule: "0 6 * * *"
                settings:
                  profile: ciso-assistant
                  profiles_path: {{Path.Combine(files, "profiles")}}
                  base_url: {{ciso.ApiUrl}}
                secrets:
                  token: file:{{token}}
            target:
              mapping: {{Path.Combine(files, "mappings", "ai6.yaml")}}
              compliance_assessment: EX-AI6-2026
            """, ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["run", "--config", config],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            _ => { },
            ct);
        var text = output.ToString();
        return (exit, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    private async Task<JsonNode> RowAsync(string refId, CancellationToken ct) => (await RowsAsync(refId, ct)).ShouldHaveSingleItem()!;

    private async Task<JsonArray> RowsAsync(string refId, CancellationToken ct) =>
        (await GetAsync($"requirement-assessments/?compliance_assessment__ref_id=EX-AI6-2026&requirement__ref_id={refId}", ct))["results"]!.AsArray();

    private static string[] Labels(JsonNode asset) => [.. asset["filtering_labels"]!.AsArray().Select(l => l!["str"]!.GetValue<string>())];

    private static async Task WriteResultAsync(string name, JsonNode content, CancellationToken ct)
    {
        var folder = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "ai6")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, name), content.ToJsonString(new() { WriteIndented = true }), ct);
    }

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
