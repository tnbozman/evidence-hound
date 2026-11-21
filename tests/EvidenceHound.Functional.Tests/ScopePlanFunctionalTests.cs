using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-07 and s06-08: <c>hound scope plan</c> on the example scope against the seeded CISO Assistant lists what applying it would create,
/// and what the seed holds that the scope does not declare; <c>hound scope apply</c> then writes the structure, and a second apply writes
/// nothing. One test, so the plan always sees the seed before the apply changes it. The plans and reports land in
/// <c>test-results/scope-plan/</c>, which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ScopePlanFunctionalTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task Plan_lists_the_expected_creates_then_apply_writes_them_and_a_second_apply_writes_nothing()
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

        var (firstExit, first) = await RunAsync("apply", scope, ct);
        var (secondExit, second) = await RunAsync("apply", scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "first-apply.json"), first, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "second-apply.json"), second, ct);

        (firstExit, secondExit).ShouldBe((ExitCodes.Ok, ExitCodes.Ok), first + second);
        JsonNode.Parse(first)!["changes"]!.AsArray()
            .Where(c => c!["outcome"]!.GetValue<string>() == "written")
            .Select(c => (c!["action"]!.GetValue<string>(), c["kind"]!.GetValue<string>(), c["key"]!.GetValue<string>()))
            .ShouldBe(
            [
                ("update", "domain", "Example Pty Ltd"),
                ("create", "perimeter", "Example Pty Ltd/Corporate IT"),
                ("create", "system", "Example 365"), ("create", "system", "Example laptops"), ("create", "system", "Example backup service"),
            ],
            ignoreOrder: true,
            first);
        var again = JsonNode.Parse(second)!;
        (again["written"]!.GetValue<int>(), again["failed"]!.GetValue<int>()).ShouldBe((0, 0), second);
        again["changes"]!.AsArray().ShouldBeEmpty(second);
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
        return (exitCode, output.ToString() + error.ToString());
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
