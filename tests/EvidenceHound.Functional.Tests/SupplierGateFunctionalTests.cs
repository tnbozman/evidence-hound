using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-20: a fictional supplier with a yearly review, applied to the local CISO Assistant on a fixed day past its first due date, opens the
/// next review with its questionnaire. A system the supplier provides is refused until the GRC team concludes the review in CISO Assistant,
/// then plans and applies. Outputs land in <c>test-results/supplier-gate/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SupplierGateFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string Supplier = """
        ref_id: SUP-FT-GATE
        name: Example Hosting Pty Ltd
        domain: Example Pty Ltd
        dependency: 3
        penetration: 2
        maturity: 3
        trust: 3
        solutions:
          - ref_id: SOL-FT-GATE
            name: Hosted rostering
            criticality: 3
        contracts:
          - ref_id: CTR-FT-GATE
            name: Rostering subscription
            status: active
            solutions: [SOL-FT-GATE]
        assessments:
          - ref_id: EA-FT-GATE
            name: Example Hosting review
            solutions: [SOL-FT-GATE]
            perimeter: Corporate IT
            due_date: "2026-06-30"
            criticality: 3
            questionnaire: urn:example:risk:framework:example-baseline
            review_every: 12 months
        """;

    internal const string System = "      - name: Example rostering\n        ref_id: SYS-ROSTER\n        type: primary\n        solution: SOL-FT-GATE\n";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-supplier-gate-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "supplier-gate")).FullName;

    [Fact]
    public async Task A_due_review_opens_with_its_questionnaire_and_a_system_waits_until_its_supplier_is_concluded_ok()
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
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "sup-ft-gate.yaml"), Supplier, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        // Past the first review's due date: applying sets up that review and opens the next one, each with its questionnaire audit.
        var (applyExit, applied) = await StepAsync("1-apply-supplier", ["scope", "apply", scope, "--config", config], ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);
        var reviews = (await SendAsync(HttpMethod.Get, "entity-assessments/?entity__ref_id=SUP-FT-GATE", null, ct))["results"]!.AsArray()
            .Where(r => r!["ref_id"]?.GetValue<string>() is "EA-FT-GATE-2026-06" or "EA-FT-GATE-2027-06").ToArray();
        reviews.Select(r => r!["ref_id"]!.GetValue<string>()).ShouldBe(["EA-FT-GATE-2026-06", "EA-FT-GATE-2027-06"], ignoreOrder: true);
        reviews.ShouldAllBe(r => r!["compliance_assessment"] != null);

        // A system the supplier provides: refused while no review is concluded.
        var setup = Path.Combine(scope, "setup.yaml");
        var text = await File.ReadAllTextAsync(setup, ct);
        await File.WriteAllTextAsync(setup, text.Replace("    systems:\n", "    systems:\n" + System, StringComparison.Ordinal), ct);
        var (refusedExit, refused) = await StepAsync("2-plan-refused", ["scope", "plan", scope, "--config", config], ct);
        refusedExit.ShouldBe(ExitCodes.Configuration, refused);
        refused.ShouldContain("system.supplier_not_approved");

        // The GRC team concludes the first review as ok in CISO Assistant; the next is not yet due.
        var first = reviews.Single(r => r!["ref_id"]!.GetValue<string>() == "EA-FT-GATE-2026-06")!["id"]!.GetValue<string>();
        await SendAsync(HttpMethod.Patch, $"entity-assessments/{first}/", new JsonObject { ["conclusion"] = "ok" }, ct);

        var (planExit, plan) = await StepAsync("3-plan-approved", ["scope", "plan", scope, "--config", config], ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("| Create | system | Example rostering |");
        var (systemExit, system) = await StepAsync("4-apply-system", ["scope", "apply", scope, "--config", config], ct);
        systemExit.ShouldBe(ExitCodes.Ok, system);
        JsonNode.Parse(system)!["failed"]!.GetValue<int>().ShouldBe(0, system);

        var (_, again) = await StepAsync("5-plan-after-apply", ["scope", "plan", scope, "--config", config], ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = "2026-07-15" },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }

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
}
