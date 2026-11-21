using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-07 to s06-09: the fixture seeds CISO Assistant by applying the example scope with <c>hound scope apply</c>. That apply wrote every kind
/// of object; after it, <c>hound scope plan</c> lists no changes and a second apply writes nothing, even after the other functional tests
/// have published runs to the same audits. The seed's report, the plans and the second report land in <c>test-results/scope-plan/</c>,
/// which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ScopePlanFunctionalTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task The_seed_applies_every_kind_then_plan_lists_no_changes_and_apply_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = CisoAssistantFixture.ExampleScope;
        var output = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "scope-plan")).FullName;

        var seed = JsonNode.Parse(ciso.SeedApplyReport)!;
        seed["failed"]!.GetValue<int>().ShouldBe(0, ciso.SeedApplyReport);
        var written = seed["changes"]!.AsArray()
            .Where(c => c!["outcome"]!.GetValue<string>() == "written")
            .Select(c => (Kind: c!["kind"]!.GetValue<string>(), Key: c["key"]!.GetValue<string>()))
            .ToArray();
        written.Select(c => c.Kind).Distinct().ShouldBe(
            ["framework", "domain", "perimeter", "system", "audit", "document", "control", "assessment"], ignoreOrder: true, ciso.SeedApplyReport);
        written.ShouldContain(("framework", "urn:example:risk:framework:example-baseline"), ciso.SeedApplyReport);
        written.ShouldContain(("audit", "EX-ESSENTIALS-2026"), ciso.SeedApplyReport);
        written.ShouldContain(("document", "identity-policy"), ciso.SeedApplyReport);
        written.ShouldContain(("control", "AC-AUTO-EVIDENCE"), ciso.SeedApplyReport);
        written.ShouldContain(("assessment", "EX-ESSENTIALS-2026/IT.GOV-01.1"), ciso.SeedApplyReport);
        written.ShouldContain(("assessment", "EX-E8-2026/ISM-1486"), ciso.SeedApplyReport);
        // CISO Assistant may load the mapping library itself when both its frameworks are loaded; either way it is loaded now.
        (await LoadedLibraryUrnsAsync(ct)).ShouldContain("urn:intuitem:risk:library:mapping-essential-eight-2023-and-asd-essentials-enterprise-it");

        var (jsonExit, json) = await PlanAsync(scope, ct, "--format", "json");
        var (markdownExit, markdown) = await PlanAsync(scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.json"), json, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.md"), markdown, ct);

        (jsonExit, markdownExit).ShouldBe((ExitCodes.Ok, ExitCodes.Ok), json + markdown);
        JsonNode.Parse(json)!["changes"]!.AsArray().ShouldBeEmpty(json);
        markdown.ShouldStartWith("## Scope plan\n\n0 to create, 0 to update, 0 to delete.");

        var (applyExit, apply) = await RunAsync("apply", scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "second-apply.json"), apply, ct);

        applyExit.ShouldBe(ExitCodes.Ok, apply);
        var again = JsonNode.Parse(apply)!;
        (again["written"]!.GetValue<int>(), again["failed"]!.GetValue<int>()).ShouldBe((0, 0), apply);
        again["changes"]!.AsArray().ShouldBeEmpty(apply);
    }

    private async Task<List<string>> LoadedLibraryUrnsAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, "loaded-libraries/?limit=1000"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return [.. (body is JsonObject page ? page["results"]! : body).AsArray().Select(l => l!["urn"]!.GetValue<string>())];
    }

    private Task<(int ExitCode, string Output)> PlanAsync(string scope, CancellationToken ct, params string[] args) => RunAsync("plan", scope, ct, args);

    private async Task<(int ExitCode, string Output)> RunAsync(string command, string scope, CancellationToken ct, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["scope", command, scope, .. args],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        // Logs go to the error writer; on a failure they are what explains it.
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
