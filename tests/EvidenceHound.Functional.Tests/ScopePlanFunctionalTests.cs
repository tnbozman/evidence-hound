using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-07: <c>hound scope plan</c> on the example scope against the seeded CISO Assistant lists what applying it would create, and what
/// the seed holds that the scope does not declare. The Markdown and JSON plans land in <c>test-results/scope-plan/</c>, which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ScopePlanFunctionalTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task Plan_of_the_example_scope_lists_the_expected_creates_and_the_seed_as_drift()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = RepoRoot();
        var scope = Path.Combine(root, "examples", "compliance-repo", "scope");
        var output = Directory.CreateDirectory(Path.Combine(root, "test-results", "scope-plan")).FullName;

        var (jsonExit, json) = await PlanAsync(scope, ct, "--format", "json");
        var (markdownExit, markdown) = await PlanAsync(scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.json"), json, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.md"), markdown, ct);

        (jsonExit, markdownExit).ShouldBe((ExitCodes.Ok, ExitCodes.Ok), json + markdown);
        var plan = JsonNode.Parse(json)!;
        var changes = plan["changes"]!.AsArray().Select(c => (Action: c!["action"]!.GetValue<string>(), Kind: c["kind"]!.GetValue<string>(), Key: c["key"]!.GetValue<string>())).ToArray();
        changes.Where(c => c.Action == "create").Select(c => (c.Kind, c.Key)).ShouldBe(
        [
            ("perimeter", "Example Pty Ltd/Corporate IT"),
            ("system", "Example 365"), ("system", "Example laptops"), ("system", "Example backup service"),
            ("document", "identity-policy"), ("document", "backup-procedure"),
        ],
        ignoreOrder: true,
        json);
        changes.ShouldContain(("update", "domain", "Example Pty Ltd"), json);
        changes.ShouldContain(("update", "audit", "EX-E8-2026"), json);
        changes.ShouldNotContain(c => c.Action == "delete", json);
        changes.ShouldNotContain(c => c.Kind == "framework", json);
        var drift = plan["drift"]!.AsArray().Select(d => (Kind: d!["kind"]!.GetValue<string>(), Key: d["key"]!.GetValue<string>())).ToArray();
        drift.ShouldContain(("perimeter", "Example Pty Ltd/Example baseline"), json);
        drift.ShouldContain(("audit", "EXAMPLE-BASELINE-2026"), json);
        drift.ShouldContain(("control", "AC-AUTO-EVIDENCE"), json);
        markdown.ShouldStartWith("## Scope plan\n");
        markdown.ShouldContain("| Create | perimeter | Example Pty Ltd/Corporate IT |");
    }

    private async Task<(int ExitCode, string Output)> PlanAsync(string scope, CancellationToken ct, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["scope", "plan", scope, .. args],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : error.ToString());
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No global.json above the test folder.");
    }
}
