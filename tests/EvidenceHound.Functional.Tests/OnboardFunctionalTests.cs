using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-24: a system onboarded without a model, from a copy of the example compliance repo, then planned and applied to the local CISO
/// Assistant as any other scope edit. Re-planning after the apply writes nothing. Outputs land in <c>test-results/onboard/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class OnboardFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string Supplier = """
        ref_id: SUP-FT-ASSETS
        name: Example Assets Ltd
        dependency: 2
        penetration: 1
        maturity: 3
        trust: 3
        solutions:
          - ref_id: SOL-FT-ASSETS
            name: Asset inventory
            criticality: 2
        contracts:
          - ref_id: CTR-FT-ASSETS
            name: Asset inventory subscription
            status: active
            solutions: [SOL-FT-ASSETS]
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-onboard-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "onboard")).FullName;

    [Fact]
    public async Task An_onboarded_system_plans_and_applies_and_reapply_writes_nothing()
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
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "sup-ft-assets.yaml"), Supplier, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        // Who and what, the supplier (already approved), then: the first of the 107 ML2 requirements not applicable, the other 106 manual,
        // and the default domain and perimeter for the system's audit.
        var answers = "Alex Example\nAlex Example\nExample Asset API\nSUP-FT-ASSETS\nExample Assets Ltd\nSOL-FT-ASSETS\nAsset inventory\nCTR-FT-ASSETS\nAsset inventory subscription\n"
            + "n\nNo internet-facing services.\nThe asset inventory is not internet-facing.\nSam Example\n"
            + string.Concat(Enumerable.Repeat("m\nThe owner exports it monthly.\n", 106))
            + "\n\n";
        string[] onboard =
        [
            "onboard", "EXAMPLE-ASSETS", "--repo", _repo, "--group", "1", "--group", "2", "--no-model",
            "--spec", Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"),
        ];
        var (onboardExit, onboarded) = await StepAsync("1-onboard", onboard, answers, ct);
        onboardExit.ShouldBe(ExitCodes.Ok, onboarded);
        onboarded.ShouldContain("Wrote scope/audits/example-assets-essential-eight-2023.yaml");

        var (validateExit, validated) = await StepAsync("2-validate", ["scope", "validate", scope, "--config", config], "", ct);
        validateExit.ShouldBe(ExitCodes.Ok, validated);

        var (planExit, plan) = await StepAsync("3-plan", ["scope", "plan", scope, "--config", config], "", ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023");

        var (applyExit, applied) = await StepAsync("4-apply", ["scope", "apply", scope, "--config", config], "", ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        var (_, again) = await StepAsync("5-plan-after-apply", ["scope", "plan", scope, "--config", config], "", ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, string input, CancellationToken ct)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            reader,
            output,
            error,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
