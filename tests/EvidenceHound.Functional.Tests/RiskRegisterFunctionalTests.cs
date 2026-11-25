using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-02: a fictional risk study with three scenarios, in a copy of the example repo, is applied to CISO Assistant, which places each on the
/// 4x4 matrix as rated. Changing one rating in the file plans one change, shown by level name, and applies as a single update. Each step's
/// output lands in <c>test-results/risk-register/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class RiskRegisterFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Study = """
        ref_id: EX-RISK-FUNC
        name: Example Pty Ltd risk register (functional test)
        domain: Example Pty Ltd
        perimeter: Corporate IT
        matrix: urn:example:risk:matrix:example-register-4x4
        status: in_progress
        authors: [it@example.com]
        scenarios:
          - ref_id: R-01
            name: Ransomware encrypts the file shares
            systems: [Example 365]
            threats: [REGISTER-THR-02]
            existing_controls: [AC-BACKUP]
            current: {probability: Possible, impact: Major}
            residual: {probability: Unlikely, impact: Major}
            treatment: mitigate
            justification: Backups are restored and tested each month.
          - ref_id: R-02
            name: The only backup administrator leaves (register test)
            systems: [Example backup service]
            threats: [REGISTER-THR-01]
            current: {probability: Unlikely, impact: Moderate}
            treatment: accept
            justification: A second administrator is being trained.
          - ref_id: R-03
            name: Staff laptop lost while travelling
            systems: [Example laptops]
            current: {probability: Likely, impact: Minor}
            treatment: open
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-risk-register-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "risk-register")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task Study_is_applied_and_a_rating_change_is_one_update_shown_by_level_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await WriteRepoAsync(ct);

        var first = await StepAsync("1-apply", ["scope", "apply", Scope], ct);
        first.Exit.ShouldBe(ExitCodes.Ok, first.Output + first.Error);
        var held = await ScenariosAsync(ct);
        held.Keys.Order(StringComparer.Ordinal).ShouldBe(["R-01", "R-02", "R-03"]);
        held["R-01"]!["current_proba"]!["name"]!.GetValue<string>().ShouldBe("Possible");
        held["R-01"]!["current_level"]!["name"]!.GetValue<string>().ShouldBe("High", "CISO Assistant computes the level from the 4x4 grid");
        held["R-03"]!["current_level"]!["name"]!.GetValue<string>().ShouldBe("Medium");
        held["R-02"]!["residual_level"]!["name"]!.GetValue<string>().ShouldBe("--");

        var risks = Path.Combine(Scope, "risks", "ex-risk-func.yaml");
        await File.WriteAllTextAsync(risks, (await File.ReadAllTextAsync(risks, ct)).Replace("{probability: Likely, impact: Minor}", "{probability: Possible, impact: Minor}", StringComparison.Ordinal), ct);
        var plan = await StepAsync("2-plan-one-rating", ["scope", "plan", Scope, "--format", "json"], ct);
        plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
        var change = JsonNode.Parse(plan.Output)!["changes"]!.AsArray().Where(c => c!["kind"]!.GetValue<string>().StartsWith("risk", StringComparison.Ordinal)).ShouldHaveSingleItem()!;
        change["key"]!.GetValue<string>().ShouldBe("EX-RISK-FUNC/R-03");
        change["fields"]!.ToJsonString().ShouldContain("Likely");
        change["fields"]!.ToJsonString().ShouldContain("Possible");

        var applied = await StepAsync("3-apply-one-rating", ["scope", "apply", Scope], ct);
        applied.Exit.ShouldBe(ExitCodes.Ok, applied.Output + applied.Error);
        JsonNode.Parse(applied.Output)!["changes"]!.AsArray().Where(c => c!["kind"]!.GetValue<string>().StartsWith("risk", StringComparison.Ordinal))
            .ShouldHaveSingleItem()!["outcome"]!.GetValue<string>().ShouldBe("written");
        (await ScenariosAsync(ct))["R-03"]!["current_proba"]!["name"]!.GetValue<string>().ShouldBe("Possible");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task WriteRepoAsync(CancellationToken ct)
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        // Its own library URNs, so this test and the library test never share a stored library.
        var library = await ExampleRiskLibrary.CopyAsync("register", ct);
        Directory.CreateDirectory(Path.Combine(_repo, "libraries"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "libraries", "example-register.yaml"), library, ct);
        var setup = Path.Combine(Scope, "setup.yaml");
        var text = await File.ReadAllTextAsync(setup, ct);
        text = text.Contains("\nlibraries:\n", StringComparison.Ordinal)
            ? text.Replace("\nlibraries:\n", "\nlibraries:\n  - urn: urn:example:risk:library:example-register\n    file: ../libraries/example-register.yaml\n", StringComparison.Ordinal)
            : text + "libraries:\n  - urn: urn:example:risk:library:example-register\n    file: ../libraries/example-register.yaml\n";
        await File.WriteAllTextAsync(setup, text, ct);
        Directory.CreateDirectory(Path.Combine(Scope, "risks"));
        await File.WriteAllTextAsync(Path.Combine(Scope, "risks", "ex-risk-func.yaml"), Study.TrimStart() + "\n", ct);
    }

    private async Task<Dictionary<string, JsonNode>> ScenariosAsync(CancellationToken ct)
    {
        var study = (await GetAsync("risk-assessments/?limit=500", ct))["results"]!.AsArray().Single(r => r!["ref_id"]?.GetValue<string>() == "EX-RISK-FUNC")!;
        var scenarios = (await GetAsync($"risk-scenarios/?risk_assessment={study["id"]!.GetValue<string>()}", ct))["results"]!.AsArray();
        return scenarios.ToDictionary(s => s!["ref_id"]!.GetValue<string>(), s => s!, StringComparer.Ordinal);
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
