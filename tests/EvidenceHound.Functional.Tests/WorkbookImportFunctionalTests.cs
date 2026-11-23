using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-41 functional check: the fictional audit workbook in <c>examples/workbooks/</c> imported into a copy of the example repo, the row
/// without an approver approved with <c>hound approve</c> on a terminal, the scope still valid, and the plan against the local CISO
/// Assistant showing each imported row with its provenance. Nothing is applied. Outputs land in <c>test-results/workbook-import/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class WorkbookImportFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    internal const string Today = "2026-11-23";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-workbook-import-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "workbook-import")).FullName;

    [Fact]
    public async Task An_imported_workbook_validates_and_the_plan_shows_each_row_with_its_provenance()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");
        var workbook = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "workbooks", "example-audit-workbook.csv");

        var (importExit, imported) = await StepAsync("1-import", ["scope", "import", workbook, "--repo", _repo, "--audit", "EX-E8-2026"], "", terminal: false, ct);
        importExit.ShouldBe(ExitCodes.Ok, imported);
        imported.ShouldContain("2 row(s) imported with the workbook's approver, 1 drafted for 'hound approve'");

        // The row no one approved in the workbook: an agent cannot approve it, a person on a terminal does.
        var (refusedExit, _) = await StepAsync("2-approve-without-a-terminal", ["approve", "--repo", _repo], "Alex Example\ny\n", terminal: false, ct);
        refusedExit.ShouldBe(ExitCodes.Usage);
        var (approveExit, approved) = await StepAsync("3-approve", ["approve", "--repo", _repo], "Alex Example\ny\n", terminal: true, ct);
        approveExit.ShouldBe(ExitCodes.Ok, approved);
        approved.ShouldContain("Approved 1 of 1.");

        var (validateExit, validated) = await StepAsync("4-validate", ["scope", "validate", scope, "--config", config], "", terminal: false, ct);
        validateExit.ShouldBe(ExitCodes.Ok, validated);

        var (planExit, plan) = await StepAsync("5-plan", ["scope", "plan", scope, "--config", config], "", terminal: false, ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("EX-E8-2026/ISM-1807");
        plan.ShouldContain("Imported from example-audit-workbook.csv, row 2, approved by Sam Example on 2026-05-01.");
        plan.ShouldContain("Imported from example-audit-workbook.csv, row 3, approved by Sam Example on 2026-05-01.");
        plan.ShouldContain($"Imported from example-audit-workbook.csv, row 4, approved by Alex Example on {Today}.");
        plan.ShouldNotContain("EX-0001");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, string input, bool terminal, CancellationToken ct)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            reader,
            output,
            error,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = Today },
            terminal,
            ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
