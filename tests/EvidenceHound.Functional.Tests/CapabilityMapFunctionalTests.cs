using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-28: on a copy of the example repo, with recorded Microsoft Graph answers and no model, <c>hound onboard environment</c> records
/// application control as provided by a third-party product and patch applications as a gap; <c>hound scope plan</c> against the local CISO
/// Assistant then shows the Microsoft 365 audit's application control rows as open, met by that product (s09-36), lists them as open
/// dependencies, and lists the gap.
/// Outputs land in <c>test-results/capability-map/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class CapabilityMapFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string Signals = """
        {"source": "Microsoft Graph (recorded)", "licences": ["SPE_E3"], "configuration_types": []}
        """;

    // Who decides and the approving role, then pa, pos, mfa, rap, ac, rmom, uah, rb.
    internal const string Answers = "Alex Example\nGRC lead\ng\na\na\nm\ns\nExample app control\nSUP-EXAPPCTL\na\na\ns\nExample backup service\n";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-capability-map-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "capability-map")).FullName;

    [Fact]
    public async Task Application_control_from_a_third_party_stays_open_on_microsoft_365_as_a_dependency_and_the_gap_is_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        await File.WriteAllTextAsync(Path.Combine(_repo, "signals.json"), Signals, ct);
        var (onboardExit, onboarded) = await StepAsync(
            "1-onboard-environment",
            ["onboard", "environment", "--repo", _repo, "--signals", Path.Combine(_repo, "signals.json"), "--microsoft-system", "Example 365"],
            Answers,
            ct);
        onboardExit.ShouldBe(ExitCodes.Ok, onboarded);
        onboarded.ShouldContain("Gap: pa has no product");

        var (planExit, plan) = await StepAsync("2-scope-plan", ["scope", "plan", Path.Combine(_repo, "scope"), "--config", Path.Combine(_repo, "hound.yaml")], "", ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("### Capability gaps");
        plan.ShouldContain("| pa |");
        // s09-36: rows another system meets stay open, naming it, and the plan lists them as open dependencies.
        plan.ShouldContain("Met by Example app control");
        plan.ShouldContain("### Open dependencies");
        plan.ShouldContain("| Example app control | (none) | no audit yet |");
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
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["XDG_CONFIG_HOME"] = Path.Combine(_repo, ".home") },
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, output.ToString() + error.ToString());
    }
}
