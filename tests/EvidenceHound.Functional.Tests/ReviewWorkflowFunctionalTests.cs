using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-12: docs/scope.md's review workflow, followed as written: copy the example compliance repo, change a system's owner, then validate
/// (passes), plan (one field update) and apply (writes it). The example scope is applied again afterwards, so the shared instance is as seeded.
/// Outputs land in <c>test-results/review-workflow/</c>, which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ReviewWorkflowFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("hound-review-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "review-workflow")).FullName;

    [Fact]
    public async Task Changing_a_system_owner_validates_plans_one_update_and_applies_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var setup = Path.Combine(_repo, "scope", "setup.yaml");
        var text = await File.ReadAllTextAsync(setup, ct);
        var laptops = text.IndexOf("- name: Example laptops", StringComparison.Ordinal);
        var owner = text.IndexOf("owner: it@example.com", laptops, StringComparison.Ordinal);
        await File.WriteAllTextAsync(setup, string.Concat(text.AsSpan(0, owner), "owner: ceo@example.com", text.AsSpan(owner + "owner: it@example.com".Length)), ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        try
        {
            var (validateExit, validated) = await StepAsync("1-validate", ["scope", "validate", scope, "--config", config], ct);
            validateExit.ShouldBe(ExitCodes.Ok, validated);

            var (planExit, plan) = await StepAsync("2-plan", ["scope", "plan", scope, "--config", config], ct);
            planExit.ShouldBe(ExitCodes.Ok, plan);
            plan.ShouldContain("0 to create, 1 to update, 0 to delete.");
            plan.ShouldContain("| Update | system | Example laptops | owner: it@example.com → ceo@example.com |");

            var (applyExit, applied) = await StepAsync("3-apply", ["scope", "apply", scope, "--config", config], ct);
            applyExit.ShouldBe(ExitCodes.Ok, applied);
            JsonNode.Parse(applied)!["written"]!.GetValue<int>().ShouldBe(1, applied);

            var (_, after) = await StepAsync("4-plan-after-apply", ["scope", "plan", scope, "--config", config], ct);
            after.ShouldContain("0 to create, 0 to update, 0 to delete.");
        }
        finally
        {
            await StepAsync("5-restore", ["scope", "apply", CisoAssistantFixture.ExampleScope, "--config", Path.Combine(example, "hound.yaml")], ct);
        }
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
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
