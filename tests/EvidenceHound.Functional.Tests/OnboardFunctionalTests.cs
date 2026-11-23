using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-24: a system onboarded without a model, from a copy of the example compliance repo, then planned and applied to the local CISO
/// Assistant as any other scope edit. Re-planning after the apply writes nothing. Outputs land in <c>test-results/onboard/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class OnboardFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string Supplier = """
        ref_id: SUP-FT-ASSETS
        name: Example Assets Ltd
        dependency: 2
        penetration: 1
        maturity: 3
        trust: 3
        solutions:
          - ref_id: SOL-FT-ASSETS
            name: Asset inventory
            criticality: 2
        contracts:
          - ref_id: CTR-FT-ASSETS
            name: Asset inventory subscription
            status: active
            solutions: [SOL-FT-ASSETS]
        assessments:
          - ref_id: EA-FT-ASSETS
            name: Example Assets review
            domain: Example Pty Ltd
            solutions: [SOL-FT-ASSETS]
            perimeter: Corporate IT
            due_date: "2026-12-31"
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-onboard-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "onboard")).FullName;

    [Fact]
    public async Task An_onboarded_system_plans_and_applies_and_reapply_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var thirdParties = Directory.CreateDirectory(Path.Combine(_repo, "scope", "third-parties")).FullName;
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "sup-ft-assets.yaml"), Supplier, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        // s09-20: the supplier and its review go to CISO Assistant first, and the GRC team concludes the review there, so the system
        // onboarding declares can be registered.
        var (supplierExit, supplier) = await StepAsync("0-apply-supplier", ["scope", "apply", scope, "--config", config], "", ct);
        supplierExit.ShouldBe(ExitCodes.Ok, supplier);
        // CISO Assistant does not filter entity assessments by ref_id, and other tests keep their own reviews in the shared instance.
        var review = (await SendAsync(HttpMethod.Get, "entity-assessments/", null, ct))["results"]!.AsArray().Single(a => a!["ref_id"]?.GetValue<string>() == "EA-FT-ASSETS")!;
        await SendAsync(HttpMethod.Patch, $"entity-assessments/{review["id"]!.GetValue<string>()}/", new JsonObject { ["conclusion"] = "ok" }, ct);

        // Who and what (the owner as a CISO Assistant user's email), the supplier (already approved), then: the first of the 107 ML2 requirements not applicable, the other 106 manual,
        // and the default domain and perimeter for the system's audit.
        var answers = "Alex Example\nit@example.com\nExample Asset API\nSUP-FT-ASSETS\nExample Assets Ltd\nSOL-FT-ASSETS\nAsset inventory\nCTR-FT-ASSETS\nAsset inventory subscription\n"
            + "n\nNo internet-facing services.\nThe asset inventory is not internet-facing.\nSam Example\n"
            + string.Concat(Enumerable.Repeat("m\nThe owner exports it monthly.\n", 106))
            + "\n\n";
        string[] onboard =
        [
            "onboard", "EXAMPLE-ASSETS", "--repo", _repo, "--group", "1", "--group", "2",
            "--spec", Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"),
        ];
        var (onboardExit, onboarded) = await StepAsync("1-onboard", onboard, answers, ct);
        onboardExit.ShouldBe(ExitCodes.Ok, onboarded);
        onboarded.ShouldContain("Wrote scope/audits/example-assets-essential-eight-2023.yaml");
        (await File.ReadAllTextAsync(Path.Combine(scope, "setup.yaml"), ct)).ShouldContain("solution: SOL-FT-ASSETS");

        var (validateExit, validated) = await StepAsync("2-validate", ["scope", "validate", scope, "--config", config], "", ct);
        validateExit.ShouldBe(ExitCodes.Ok, validated);

        var (planExit, plan) = await StepAsync("3-plan", ["scope", "plan", scope, "--config", config], "", ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023");

        // s09-30: the plan explained for review quotes the plan's own counts.
        var saved = Path.Combine(_repo, "plan.md");
        await File.WriteAllTextAsync(saved, plan, ct);
        var (explainExit, explained) = await StepAsync("3b-explain", ["explain", saved, "--repo", _repo], "", ct);
        explainExit.ShouldBe(ExitCodes.Ok, explained);
        var counts = System.Text.RegularExpressions.Regex.Match(plan, @"\d+ to create, \d+ to update, \d+ to delete").Value;
        explained.ShouldContain($"The plan is the source of truth: {counts}");

        var (applyExit, applied) = await StepAsync("4-apply", ["scope", "apply", scope, "--config", config], "", ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        var (_, again) = await StepAsync("5-plan-after-apply", ["scope", "plan", scope, "--config", config], "", ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
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
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonNode.Parse(text)!;
    }

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, string input, CancellationToken ct)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            reader,
            output,
            error,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
