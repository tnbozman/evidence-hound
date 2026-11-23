using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-32 functional check: the onboarding tutorial's files, over a copy of the template, against the local CISO Assistant. The suppliers
/// are applied first; once the GRC team concludes their reviews as ok, the plan shows the two onboarded systems and their audits as new.
/// Outputs land in <c>test-results/onboarding-tutorial/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class OnboardingTutorialFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string[] Suppliers = ["sup-oracle.yaml", "sup-sophos.yaml", "sup-ciso-assistant.yaml", "sup-evidence-hound.yaml"];

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-onboarding-tutorial-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "onboarding-tutorial")).FullName;

    [Fact]
    public async Task Once_the_suppliers_are_approved_the_plan_creates_the_onboarded_systems_and_their_audits()
    {
        var ct = TestContext.Current.CancellationToken;
        var template = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        var tutorial = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "onboarding-tutorial");
        Copy(template, _repo);
        var scope = Path.Combine(_repo, "scope");
        var thirdParties = Directory.CreateDirectory(Path.Combine(scope, "third-parties")).FullName;
        foreach (var supplier in Suppliers)
        {
            File.Copy(Path.Combine(tutorial, "scope", "third-parties", supplier), Path.Combine(thirdParties, supplier));
        }

        // The suppliers' own pull request: the template's config, with the four supplier files.
        var (applyExit, applied) = await StepAsync("1-apply-suppliers", ["scope", "apply", scope, "--config", Path.Combine(_repo, "hound.yaml")], ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        // The GRC team concludes the Oracle and Sophos reviews as ok in CISO Assistant.
        foreach (var supplier in new[] { "SUP-ORACLE", "SUP-SOPHOS" })
        {
            var reviews = (await SendAsync(HttpMethod.Get, $"entity-assessments/?entity__ref_id={supplier}", null, ct))["results"]!.AsArray();
            reviews.ShouldNotBeEmpty($"{supplier} has its review");
            foreach (var review in reviews)
            {
                await SendAsync(HttpMethod.Patch, $"entity-assessments/{review!["id"]!.GetValue<string>()}/", new JsonObject { ["conclusion"] = "ok" }, ct);
            }
        }

        // The systems' pull request: everything the tutorial writes.
        Copy(tutorial, _repo);
        var (planExit, plan) = await StepAsync("2-plan-systems", ["scope", "plan", scope, "--config", Path.Combine(_repo, "hound.yaml")], ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("| Create | system | GRC-PLATFORM |");
        plan.ShouldContain("| Create | system | OFFICE-FIREWALL |");
        plan.ShouldContain("| Create | audit | GRC-PLATFORM-ESSENTIAL-EIGHT-2023 |");
        plan.ShouldContain("| Create | audit | OFFICE-FIREWALL-ESSENTIAL-EIGHT-2023 |");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }
    }

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = "2026-11-23" },
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
