using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-03: a fictional security exception and an accepted risk, in a copy of the example repo, are applied to CISO Assistant as requests:
/// draft, with their links and no approval. An approver then approves the exception in CISO Assistant, and the next plan shows it approved
/// and changes nothing. Each step's output lands in <c>test-results/security-exceptions/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SecurityExceptionFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Study = """
        ref_id: EX-RISK-EXC
        name: Example Pty Ltd risks with exceptions (functional test)
        domain: Example Pty Ltd
        matrix: urn:example:risk:matrix:example-exception-4x4
        scenarios:
          - ref_id: R-01
            name: Finance app sign-in without MFA
            systems: [Example 365]
            current: {probability: Possible, impact: Major}
            treatment: mitigate
          - ref_id: R-02
            name: The only backup administrator leaves
            current: {probability: Unlikely, impact: Moderate}
            treatment: accept
            justification: A second administrator is being trained.
        """;

    private static readonly string Exceptions = """
        exceptions:
          - ref_id: EXC-FUNC-01
            name: Legacy finance app without MFA
            domain: Example Pty Ltd
            severity: medium
            owners: [it@example.com]
            expiration_date: "2027-03-31"
            observation: Sign-in is limited to the office network.
            systems: [Example 365]
            controls: [AC-MFA]
            scenarios: [EX-RISK-EXC/R-01]
            requirements:
              - audit: EX-E8-2026
                requirement: ISM-1504
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-exceptions-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "security-exceptions")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task Exception_and_acceptance_are_requested_and_the_plan_shows_the_approvers_decision()
    {
        var ct = TestContext.Current.CancellationToken;
        await WriteRepoAsync(ct);

        var applied = await StepAsync("1-apply", ["scope", "apply", Scope], ct);
        applied.Exit.ShouldBe(ExitCodes.Ok, applied.Output + applied.Error);
        var exception = await OneAsync("security-exceptions", "EXC-FUNC-01", ct);
        exception["status"]!.GetValue<string>().ShouldBe("draft", "hound only requests; an approver decides");
        var acceptance = await OneAsync("risk-acceptances", "ACC-EX-RISK-EXC-R-02", ct);
        acceptance["state"]!.GetValue<string>().ShouldNotBe("accepted");

        await PatchAsync($"security-exceptions/{exception["id"]!.GetValue<string>()}/", new JsonObject { ["status"] = "approved" }, ct);
        var plan = await StepAsync("2-plan-after-approval", ["scope", "plan", Scope, "--format", "json"], ct);

        plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
        var json = JsonNode.Parse(plan.Output)!;
        json["changes"]!.AsArray().Where(c => c!["kind"]!.GetValue<string>() is "security_exception" or "risk_acceptance" or "risk_scenario").ShouldBeEmpty(plan.Output);
        json["exceptions"]!.AsArray().Single(n => n!["ref_id"]!.GetValue<string>() == "EXC-FUNC-01" && n["code"]!.GetValue<string>() == "exception.status")!
            ["status"]!.GetValue<string>().ShouldBe("approved");
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

        // Its own library URNs, so this test never shares a stored library with the other risk tests.
        var library = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-risk-library.yaml"), ct))
            .Replace("example-risk", "example-exception", StringComparison.Ordinal)
            .Replace("example-4x4", "example-exception-4x4", StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(_repo, "libraries"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "libraries", "example-exception.yaml"), library, ct);
        var setup = Path.Combine(Scope, "setup.yaml");
        var text = await File.ReadAllTextAsync(setup, ct);
        text = text.Contains("\nlibraries:\n", StringComparison.Ordinal)
            ? text.Replace("\nlibraries:\n", "\nlibraries:\n  - urn: urn:example:risk:library:example-exception\n    file: ../libraries/example-exception.yaml\n", StringComparison.Ordinal)
            : text + "libraries:\n  - urn: urn:example:risk:library:example-exception\n    file: ../libraries/example-exception.yaml\n";
        await File.WriteAllTextAsync(setup, text, ct);
        Directory.CreateDirectory(Path.Combine(Scope, "risks"));
        await File.WriteAllTextAsync(Path.Combine(Scope, "risks", "ex-risk-exc.yaml"), Study.TrimStart() + "\n", ct);
        await File.WriteAllTextAsync(Path.Combine(Scope, "exceptions.yaml"), Exceptions.TrimStart() + "\n", ct);
    }

    private async Task<JsonNode> OneAsync(string resource, string refId, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Get, $"{resource}/?limit=500", null, ct))["results"]!.AsArray().Single(o => o!["ref_id"]?.GetValue<string>() == refId)!;

    private Task<JsonNode> PatchAsync(string path, JsonObject body, CancellationToken ct) => SendAsync(HttpMethod.Patch, path, body, ct);

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
