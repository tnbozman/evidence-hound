using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-D2: an audit's <c>systems:</c> are written to CISO Assistant as the audit's assets when it is created and when they change, and a
/// change made in CISO Assistant shows in the plan. Runs on an audit of its own, deleted at the end, so the shared seed is untouched.
/// Each step's output lands in <c>test-results/audit-systems/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class AuditSystemsFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    public const string RefId = "EXAMPLE-SYSTEMS-FUNC-2026";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-audit-systems-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "audit-systems")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    private string Audit => Path.Combine(Scope, "audits", "example-systems-func-2026.yaml");

    [Fact]
    public async Task An_audits_systems_are_its_assets_on_create_and_update_and_drift_shows_in_the_plan()
    {
        var ct = TestContext.Current.CancellationToken;
        CopyExampleRepo();
        await File.WriteAllTextAsync(Audit, (await File.ReadAllTextAsync(Path.Combine(Scope, "audits", "example-baseline-2026.yaml"), ct))
            .Replace("ref_id: EXAMPLE-BASELINE-2026", $"ref_id: {RefId}", StringComparison.Ordinal)
            .Replace("name: Example baseline 2026", "name: Example baseline systems test", StringComparison.Ordinal), ct);
        try
        {
            (await StepAsync("1-apply-create", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);
            (await AssetsAsync(ct)).ShouldBe(["Example 365", "Example laptops"]);

            await File.WriteAllTextAsync(Audit, (await File.ReadAllTextAsync(Audit, ct)).Replace(
                "systems: [Example 365, Example laptops]", "systems: [Example 365, Example laptops, Example backup service]", StringComparison.Ordinal), ct);
            var plan = await StepAsync("2-plan-added-system", ["scope", "plan", Scope, "--format", "json"], ct);
            plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
            Change(plan.Output)!["fields"]!.AsArray().ShouldContain(f => f!["field"]!.GetValue<string>() == "systems");
            (await StepAsync("3-apply-update", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);
            (await AssetsAsync(ct)).ShouldBe(["Example 365", "Example backup service", "Example laptops"]);

            // Someone removes a system in CISO Assistant: the plan shows it, and apply puts it back.
            var audit = await AuditAsync(ct);
            var kept = new JsonArray([.. audit["assets"]!.AsArray().Where(a => a!["str"]!.GetValue<string>() != "Example laptops").Select(a => (JsonNode)a!["id"]!.GetValue<string>())]);
            await SendAsync(HttpMethod.Patch, $"compliance-assessments/{audit["id"]!.GetValue<string>()}/", new JsonObject { ["assets"] = kept }, ct);
            plan = await StepAsync("4-plan-after-ui-edit", ["scope", "plan", Scope, "--format", "json"], ct);
            Change(plan.Output).ShouldNotBeNull("a system removed in CISO Assistant shows in the plan");
            (await StepAsync("5-apply-restore", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);
            (await AssetsAsync(ct)).ShouldBe(["Example 365", "Example backup service", "Example laptops"]);
        }
        finally
        {
            if (await FindAsync(ct) is { } created)
            {
                await SendAsync(HttpMethod.Delete, $"compliance-assessments/{created["id"]!.GetValue<string>()}/", null, ct);
            }
        }
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private static JsonNode? Change(string planJson) =>
        JsonNode.Parse(planJson)!["changes"]!.AsArray().FirstOrDefault(c => c!["key"]!.GetValue<string>() == RefId);

    private async Task<string[]> AssetsAsync(CancellationToken ct) =>
        [.. (await AuditAsync(ct))["assets"]!.AsArray().Select(a => a!["str"]!.GetValue<string>()).Order(StringComparer.Ordinal)];

    private async Task<JsonNode> AuditAsync(CancellationToken ct) => (await FindAsync(ct)).ShouldNotBeNull();

    private async Task<JsonNode?> FindAsync(CancellationToken ct)
    {
        var listed = await SendAsync(HttpMethod.Get, $"compliance-assessments/?ref_id={RefId}", null, ct);
        return listed?["results"]!.AsArray().FirstOrDefault() is { } found ? await SendAsync(HttpMethod.Get, $"compliance-assessments/{found["id"]!.GetValue<string>()}/", null, ct) : null;
    }

    private void CopyExampleRepo()
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private async Task<(int Exit, string Output, string Error)> StepAsync(string name, string[] args, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, output.ToString(), error.ToString());
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(string.Create(CultureInfo.InvariantCulture, $"{method} {path}: {(int)response.StatusCode} {text}"));
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }
}
