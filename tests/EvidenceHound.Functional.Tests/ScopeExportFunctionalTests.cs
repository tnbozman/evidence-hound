using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-11: export the seeded CISO Assistant (and whatever the other functional tests published to it) as scope files, then plan them against
/// it: no changes. A second export gives the same files. Outputs land in <c>test-results/scope-export/</c>, which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ScopeExportFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("hound-export-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "scope-export")).FullName;

    [Fact]
    public async Task Export_then_plan_lists_no_changes_and_a_second_export_is_the_same()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Path.Combine(_repo, "first", "scope");
        var again = Path.Combine(_repo, "second", "scope");

        var (exportExit, exported) = await HoundAsync(["scope", "export", "--out", scope, "--with-files"], ct);
        await File.WriteAllTextAsync(Path.Combine(_results, "export.txt"), exported, ct);
        exportExit.ShouldBe(ExitCodes.Ok, exported);
        foreach (var file in Directory.GetFiles(scope, "*.yaml", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(scope, file).Replace(Path.DirectorySeparatorChar, '_');
            File.Copy(file, Path.Combine(_results, name), overwrite: true);
        }

        var (validateExit, validated) = await HoundAsync(["scope", "validate", scope], ct);
        validateExit.ShouldBe(ExitCodes.Ok, validated);

        var (planExit, plan) = await HoundAsync(["scope", "plan", scope, "--format", "json"], ct);
        await File.WriteAllTextAsync(Path.Combine(_results, "plan-after-export.json"), plan, ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        JsonNode.Parse(plan)!["changes"]!.AsArray().ShouldBeEmpty(plan);

        (await HoundAsync(["scope", "export", "--out", again], ct)).ExitCode.ShouldBe(ExitCodes.Ok);
        foreach (var file in Directory.GetFiles(scope, "*.yaml", SearchOption.AllDirectories).Concat(Directory.GetFiles(scope, "*.json", SearchOption.AllDirectories)))
        {
            var relative = Path.GetRelativePath(scope, file);
            var second = await File.ReadAllTextAsync(Path.Combine(again, relative), ct);
            var first = await File.ReadAllTextAsync(file, ct);
            // The first export downloaded files, so its documents list them; everything else is the same.
            first.Split('\n').Where(l => !l.TrimStart().StartsWith("files:", StringComparison.Ordinal))
                .ShouldBe(second.Split('\n'), relative);
        }
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<(int ExitCode, string Output)> HoundAsync(string[] args, CancellationToken ct)
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
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
