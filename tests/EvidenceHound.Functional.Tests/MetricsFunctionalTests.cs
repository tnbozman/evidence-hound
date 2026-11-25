using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s16-02: a fictional MFA coverage metric (a formula over two counts, with a 95% target), a qualitative policy metric and a committee
/// dashboard, in a copy of the example repo, are applied to CISO Assistant. A second plan changes nothing; a new target is one update. Each
/// step's output lands in <c>test-results/metrics/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class MetricsFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Metrics = """
        definitions:
          - ref_id: EX-FMET-MFA-COVERED
            name: Accounts with MFA (functional test)
            unit: users
            direction: higher_is_better
          - ref_id: EX-FMET-MFA-TOTAL
            name: Staff accounts (functional test)
            unit: users
          - ref_id: EX-FMET-MFA
            name: MFA coverage (functional test)
            unit: percentage
            direction: higher_is_better
            decimals: 1
            frequency: monthly
            target: 95
            formula:
              expression: covered / total * 100
              inputs:
                - {key: covered, definition: EX-FMET-MFA-COVERED, combine: sum}
                - {key: total, definition: EX-FMET-MFA-TOTAL, combine: sum}
          - ref_id: EX-FMET-POLICY
            name: Policy maturity (functional test)
            levels: [Missing, Drafted, Approved, Reviewed]
            target: Approved
        instances:
          - {ref_id: EX-FMET-MFA-COVERED-CORP, name: Accounts with MFA, definition: EX-FMET-MFA-COVERED, domain: Example Pty Ltd, frequency: monthly}
          - {ref_id: EX-FMET-MFA-TOTAL-CORP, name: Staff accounts, definition: EX-FMET-MFA-TOTAL, domain: Example Pty Ltd, frequency: monthly}
          - ref_id: EX-FMET-MFA-CORP
            name: MFA coverage
            definition: EX-FMET-MFA
            domain: Example Pty Ltd
            grace_days: 5
            owners: [it@example.com]
            controls: [AC-MFA]
          - {ref_id: EX-FMET-POLICY-CORP, name: Policy maturity, definition: EX-FMET-POLICY, domain: Example Pty Ltd, frequency: quarterly}
        dashboards:
          - ref_id: EX-FDASH
            name: Security committee (functional test)
            domain: Example Pty Ltd
            widgets:
              - {title: MFA coverage, metric: EX-FMET-MFA-CORP, chart: line, time_range: last_year}
              - {title: Baseline progress, audit: EXAMPLE-BASELINE-2026, builtin: progress, x: 6}
              - {title: Policy maturity, metric: EX-FMET-POLICY-CORP, y: 2, width: 12}
              - {title: How to read this, text: Each figure is collected monthly and owned by IT., y: 4, width: 12, height: 1}
        """;

    private static readonly string[] MetricKinds = ["metric_definition", "metric_instance", "dashboard", "dashboard_widget"];

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-metrics-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "metrics")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task Metrics_and_a_dashboard_are_applied_and_a_second_plan_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await CopyExampleRepoAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(Scope, "metrics.yaml"), Metrics, ct);

        var validated = await StepAsync("1-validate", ["scope", "validate", Scope], ct);
        validated.Exit.ShouldBe(ExitCodes.Ok, validated.Output + validated.Error);
        var applied = await StepAsync("2-apply", ["scope", "apply", Scope], ct);
        applied.Exit.ShouldBe(ExitCodes.Ok, applied.Output + applied.Error);

        var definitions = await ListAsync("metrology/metric-definitions/", ct);
        var mfa = ByRef(definitions, "EX-FMET-MFA");
        mfa["unit"]!["name"]!.GetValue<string>().ShouldBe("percentage");
        mfa["default_target"]!.GetValue<double>().ShouldBe(95);
        mfa["higher_is_better"]!.GetValue<bool>().ShouldBeTrue();
        mfa["expression"]!.GetValue<string>().ShouldBe("covered / total * 100");
        mfa["inputs"]!.AsArray().Count.ShouldBe(2, mfa.ToJsonString());
        var policy = ByRef(definitions, "EX-FMET-POLICY");
        policy["category"]!.GetValue<string>().ShouldBe("qualitative");
        policy["choices_definition"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()).ShouldBe(["Missing", "Drafted", "Approved", "Reviewed"]);
        policy["default_target"]!.GetValue<double>().ShouldBe(3);

        var instance = ByRef(await ListAsync("metrology/metric-instances/", ct), "EX-FMET-MFA-CORP");
        instance["target_value"]!.GetValue<double>().ShouldBe(95);
        instance["collection_grace_days"]!.GetValue<int>().ShouldBe(5);
        instance["owner"]!.AsArray().ShouldHaveSingleItem();
        instance["applied_controls"]!.AsArray().ShouldHaveSingleItem();

        var dashboard = ByRef(await ListAsync("metrology/dashboards/", ct), "EX-FDASH");
        var widgets = (await ListAsync("metrology/dashboard-widgets/", ct)).Where(w => Id(w["dashboard"]) == dashboard["id"]!.GetValue<string>()).ToArray();
        widgets.Select(w => w["title"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .ShouldBe(["Baseline progress", "How to read this", "MFA coverage", "Policy maturity"]);
        widgets.Single(w => w["title"]!.GetValue<string>() == "Baseline progress")["metric_key"]!.GetValue<string>().ShouldBe("progress");

        var plan = await StepAsync("3-plan-again", ["scope", "plan", Scope, "--format", "json"], ct);
        plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
        MetricChanges(plan.Output).ShouldBeEmpty(plan.Output);

        // A new target is one update to the instance, and the definition it falls back from is untouched.
        await File.WriteAllTextAsync(Path.Combine(Scope, "metrics.yaml"), Metrics.Replace("    grace_days: 5\n", "    grace_days: 5\n    target: 90\n", StringComparison.Ordinal), ct);
        var changed = await StepAsync("4-plan-new-target", ["scope", "plan", Scope, "--format", "json"], ct);
        MetricChanges(changed.Output).ShouldBe([("metric_instance", "EX-FMET-MFA-CORP")], changed.Output);
        var reapplied = await StepAsync("5-apply-new-target", ["scope", "apply", Scope], ct);
        reapplied.Exit.ShouldBe(ExitCodes.Ok, reapplied.Output + reapplied.Error);
        ByRef(await ListAsync("metrology/metric-instances/", ct), "EX-FMET-MFA-CORP")["target_value"]!.GetValue<double>().ShouldBe(90);
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private static (string Kind, string Key)[] MetricChanges(string planJson) =>
        [.. JsonNode.Parse(planJson)!["changes"]!.AsArray()
            .Select(c => (Kind: c!["kind"]!.GetValue<string>(), Key: c["key"]!.GetValue<string>()))
            .Where(c => MetricKinds.Contains(c.Kind) && c.Key.Contains("EX-F", StringComparison.Ordinal))];

    private static string? Id(JsonNode? relation) => relation is JsonObject o ? o["id"]?.GetValue<string>() : relation?.GetValue<string>();

    private static JsonNode ByRef(IEnumerable<JsonNode> items, string refId) => items.Single(i => i["ref_id"]?.GetValue<string>() == refId);

    private async Task CopyExampleRepoAsync(CancellationToken ct)
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            await using var source = File.OpenRead(file);
            await using var target = File.Create(copy);
            await source.CopyToAsync(target, ct);
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

    private async Task<IEnumerable<JsonNode>> ListAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "limit=500"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["results"]!.AsArray().Select(n => n!);
    }
}
