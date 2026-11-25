using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-04: a fictional BIA for two systems, in a copy of the example repo, is applied to CISO Assistant: one asset assessment per system and
/// its escalation thresholds, with each impact on the matrix as the file names it. A second plan changes nothing. Each step's output lands in
/// <c>test-results/bia/</c>. s15-05: a recovery flag named by a check follows it as runs report it passing, then failing.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class BiaFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Bia = """
        ref_id: EX-BIA-FUNC
        name: Example Pty Ltd BIA (functional test)
        domain: Example Pty Ltd
        perimeter: Corporate IT
        matrix: urn:example:risk:matrix:example-bia-4x4
        version: "1.0"
        authors: [it@example.com]
        assessments:
          - system: Example 365
            dependencies: [Example laptops]
            controls: [AC-BACKUP]
            recovery: {documented: true, tested: false, targets_met: false}
            thresholds:
              - {after: 4h, impact: Minor}
              - {after: 1d, impact: Major, justification: Customers notice.}
          - system: Example laptops
            thresholds:
              - {after: 2d, impact: Moderate}
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-bia-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "bia")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task Bia_is_applied_with_its_thresholds_and_a_second_plan_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await WriteRepoAsync(ct);

        var applied = await StepAsync("1-apply", ["scope", "apply", Scope], ct);
        applied.Exit.ShouldBe(ExitCodes.Ok, applied.Output + applied.Error);
        var bia = (await SendAsync(HttpMethod.Get, "resilience/business-impact-analysis/?limit=500", null, ct))["results"]!.AsArray().Single(b => b!["ref_id"]?.GetValue<string>() == "EX-BIA-FUNC")!;
        var assessments = (await SendAsync(HttpMethod.Get, $"resilience/asset-assessments/?bia={bia["id"]!.GetValue<string>()}", null, ct))["results"]!.AsArray();
        assessments.Select(a => a!["asset"]!["str"]!.GetValue<string>()).Order(StringComparer.Ordinal).ShouldBe(["Example 365", "Example laptops"]);
        var main = assessments.Single(a => a!["asset"]!["str"]!.GetValue<string>() == "Example 365")!;
        main["recovery_documented"]!.GetValue<bool>().ShouldBeTrue();
        var thresholds = (await SendAsync(HttpMethod.Get, $"resilience/escalation-thresholds/?asset_assessment={main["id"]!.GetValue<string>()}", null, ct))["results"]!.AsArray();
        thresholds.Select(t => (t!["point_in_time"]!.GetValue<int>(), t["quali_impact"]!["name"]!.GetValue<string>())).OrderBy(t => t.Item1)
            .ShouldBe([(14400, "Minor"), (86400, "Major")]);

        var plan = await StepAsync("2-plan-again", ["scope", "plan", Scope, "--format", "json"], ct);
        plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
        JsonNode.Parse(plan.Output)!["changes"]!.AsArray()
            .Where(c => c!["kind"]!.GetValue<string>() is "bia" or "asset_assessment" or "escalation_threshold").ShouldBeEmpty(plan.Output);
    }

    [Fact]
    public async Task A_recovery_flag_named_by_a_check_follows_it_from_pass_to_fail_with_its_evidence()
    {
        // s15-05: the fake collector reports backup.restore_tested as pass, then (with its fail setting) as fail.
        var ct = TestContext.Current.CancellationToken;
        await WriteRepoAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(Scope, "bia", "ex-bia-flags.yaml"), """
            ref_id: EX-BIA-FLAGS
            name: Example Pty Ltd BIA (recovery flags from checks)
            domain: Example Pty Ltd
            matrix: urn:example:risk:matrix:example-bia-4x4
            assessments:
              - system: Example 365
                recovery: {documented: true, tested_by: [backup.restore_tested]}
            """, ct);
        var applied = await StepAsync("flags-1-apply", ["scope", "apply", Scope], ct);
        applied.Exit.ShouldBe(ExitCodes.Ok, applied.Output + applied.Error);

        var passed = await StepAsync("flags-2-run-pass", ["run", "--config", await TestRepo.ExampleDeploymentAsync(_repo, fail: null, ct)], ct);
        passed.Exit.ShouldBe(ExitCodes.Ok, passed.Output + passed.Error);
        // The example repo's own BIA names the same check, so its flag is set too.
        JsonNode.Parse(passed.Output)!["recovery"]!.AsArray().Single(r => r!["assessment"]!.GetValue<string>() == "EX-BIA-FLAGS/Example 365")!["value"]!.GetValue<bool>().ShouldBeTrue();
        var tested = await AssessmentAsync(ct);
        tested["recovery_tested"]!.GetValue<bool>().ShouldBeTrue();
        tested["evidences"]!.AsArray().ShouldNotBeEmpty();

        var failed = await StepAsync("flags-3-run-fail", ["run", "--config", await TestRepo.ExampleDeploymentAsync(_repo, fail: "backup.restore_tested", ct)], ct);
        failed.Exit.ShouldBe(ExitCodes.Ok, failed.Output + failed.Error);
        (await AssessmentAsync(ct))["recovery_tested"]!.GetValue<bool>().ShouldBeFalse();

        var again = await StepAsync("flags-4-run-unchanged", ["run", "--config", await TestRepo.ExampleDeploymentAsync(_repo, fail: "backup.restore_tested", ct)], ct);
        JsonNode.Parse(again.Output)!["recovery"]!.AsArray().ShouldBeEmpty();
        var plan = await StepAsync("flags-5-scope-plan", ["scope", "plan", Scope, "--format", "json"], ct);
        JsonNode.Parse(plan.Output)!["changes"]!.AsArray().ShouldNotContain(c => c!["key"]!.GetValue<string>() == "EX-BIA-FLAGS/Example 365", plan.Output);
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<JsonNode> AssessmentAsync(CancellationToken ct)
    {
        var bia = (await SendAsync(HttpMethod.Get, "resilience/business-impact-analysis/?limit=500", null, ct))["results"]!.AsArray().Single(b => b!["ref_id"]?.GetValue<string>() == "EX-BIA-FLAGS")!;
        return (await SendAsync(HttpMethod.Get, $"resilience/asset-assessments/?bia={bia["id"]!.GetValue<string>()}", null, ct))["results"]!.AsArray().ShouldHaveSingleItem()!;
    }


    private async Task WriteRepoAsync(CancellationToken ct)
    {
        TestRepo.CopyExample(_repo);

        // Its own library URNs, so this test never shares a stored library with the other risk tests.
        var library = await ExampleRiskLibrary.CopyAsync("bia", ct);
        Directory.CreateDirectory(Path.Combine(_repo, "libraries"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "libraries", "example-bia.yaml"), library, ct);
        var setup = Path.Combine(Scope, "setup.yaml");
        var text = await File.ReadAllTextAsync(setup, ct);
        text = text.Contains("\nlibraries:\n", StringComparison.Ordinal)
            ? text.Replace("\nlibraries:\n", "\nlibraries:\n  - urn: urn:example:risk:library:example-bia\n    file: ../libraries/example-bia.yaml\n", StringComparison.Ordinal)
            : text + "libraries:\n  - urn: urn:example:risk:library:example-bia\n    file: ../libraries/example-bia.yaml\n";
        await File.WriteAllTextAsync(setup, text, ct);
        Directory.CreateDirectory(Path.Combine(Scope, "bia"));
        await File.WriteAllTextAsync(Path.Combine(Scope, "bia", "ex-bia-func.yaml"), Bia.TrimStart() + "\n", ct);
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
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
