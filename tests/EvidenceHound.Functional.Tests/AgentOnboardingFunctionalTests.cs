using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-37 functional check: the example API onboarded as an agent would, with <c>supplier draft</c>, <c>pending</c> and <c>decide</c> only;
/// a person then approves everything with <c>hound approve</c> on a terminal, and one <c>hound scope apply</c> writes the supplier, its
/// review's conclusion and the system to the local CISO Assistant. Nothing is done in CISO Assistant itself. Outputs land in
/// <c>test-results/agent-onboarding/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class AgentOnboardingFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string System = "EXAMPLE-AGENT-API";
    internal const string Today = "2026-11-23";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-agent-onboarding-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "agent-onboarding")).FullName;

    [Fact]
    public async Task An_agent_drafts_a_person_approves_on_a_terminal_and_one_apply_writes_the_supplier_its_conclusion_and_the_system()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        // The agent: the supplier, then every requirement, with no prompt answered.
        string[] supplier =
        [
            "supplier", "draft", "--repo", _repo, "--system", System, "--ref", "SUP-AG-ASSETS", "--name", "Example Agent Assets Ltd", "--solution-ref", "SOL-AG-ASSETS",
            "--solution-name", "Asset inventory", "--contract-ref", "CTR-AG-ASSETS", "--contract-name", "Asset inventory subscription",
            "--level", "dependency=2:Holds the asset inventory only.", "--level", "maturity=3:Holds an ISO 27001 certificate.", "--json",
        ];
        var (draftExit, drafted) = await StepAsync("1-supplier-draft", supplier, "", terminal: false, ct);
        draftExit.ShouldBe(ExitCodes.Ok, drafted);

        string[] start =
        [
            "onboard", System, "pending", "--json", "--repo", _repo, "--group", "1",
            "--spec", Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"),
            "--owner", "it@example.com", "--product", "Example Asset API", "--solution", "SOL-AG-ASSETS",
        ];
        var (pendingExit, pending) = await StepAsync("2-pending", start, "", terminal: false, ct);
        pendingExit.ShouldBe(ExitCodes.Ok, pending);
        var requirements = JsonNode.Parse(pending)!["pending"]!.AsArray().Select(r => r!["requirement"]!.GetValue<string>()).ToArray();
        requirements.Length.ShouldBeGreaterThan(2);
        for (var i = 0; i < requirements.Length; i++)
        {
            string[] how = i switch
            {
                0 => ["--kind", "not_applicable", "--reasoning", "The asset inventory has no such component.", "--justification", "The product has none; the supplier confirms it."],
                1 => ["--kind", "api_check", "--reasoning", "GET /v1/devices lists what the requirement needs.", "--source", "GET /v1/devices"],
                _ => ["--kind", "manual", "--reasoning", "The owner exports it monthly."],
            };
            var (decideExit, decided) = await StepAsync("3-decide", ["onboard", System, "decide", requirements[i], "--repo", _repo, "--json", .. how], "", terminal: false, ct);
            decideExit.ShouldBe(ExitCodes.Ok, decided);
        }

        // The agent cannot approve: the same answers piped in are refused.
        const string answers = "Sam Example\ny\nok\ny\ny\n\n\n";
        var (refusedExit, refused) = await StepAsync("4-approve-without-a-terminal", ["approve", "--repo", _repo], answers, terminal: false, ct);
        refusedExit.ShouldBe(ExitCodes.Usage);
        refused.ShouldContain("needs a person at a terminal");

        // The person, on a terminal: the supplier, its review concluded ok, the not-applicable decision, then the system.
        var (approveExit, approved) = await StepAsync("5-approve", ["approve", "--repo", _repo], answers, terminal: true, ct);
        approveExit.ShouldBe(ExitCodes.Ok, approved);
        approved.ShouldContain("Approved 4 of 4.");

        var (validateExit, validated) = await StepAsync("6-validate", ["scope", "validate", scope, "--config", config], "", terminal: false, ct);
        validateExit.ShouldBe(ExitCodes.Ok, validated);

        var (planExit, plan) = await StepAsync("7-plan", ["scope", "plan", scope, "--config", config], "", terminal: false, ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain($"| Create | system | {System} |");
        plan.ShouldContain("| Create | supplier_assessment | EA-SUP-AG-ASSETS-2026-12 |");

        var (applyExit, applied) = await StepAsync("8-apply", ["scope", "apply", scope, "--config", config], "", terminal: false, ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        // CISO Assistant holds the conclusion the person recorded in the supplier's file, and nothing is left to change.
        var review = (await GetAsync("entity-assessments/?ref_id=EA-SUP-AG-ASSETS-2026-12", ct))["results"]!.AsArray().ShouldHaveSingleItem()!;
        review["conclusion"]!.GetValue<string>().ShouldBe("ok", StringCompareShould.IgnoreCase);
        var (_, again) = await StepAsync("9-plan-after-apply", ["scope", "plan", scope, "--config", config], "", terminal: false, ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonNode.Parse(text)!;
    }

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, string input, bool terminal, CancellationToken ct)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            reader,
            output,
            error,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = Today },
            terminal,
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
