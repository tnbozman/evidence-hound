using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-41: <c>hound scope import</c> brings an existing audit workbook into an audit's hand-written assessments. A row the workbook says
/// someone approved is written with that approver, date and row; a row without an approver stays a draft until a person approves it.
/// </summary>
public sealed class ScopeImportTests : IDisposable
{
    internal const string Workbook = """
        Requirement,Result,Observation,Evidence,Approved by,Approved on
        ISM-1807,Compliant,"Asset discovery runs daily, and its history is kept.",Discovery tool run history export,Sam Example,2026-05-01
        ISM-1808,Partially compliant,The scanner's database updates weekly.,Scanner configuration screenshot,,
        ISM-9999,Compliant,Not a requirement of this framework.,,Sam Example,2026-05-01
        ISM-1486,Compliant,Already assessed by hand in the audit.,,Sam Example,2026-05-01
        ,,,,,
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-scope-import-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ScopeImportTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(_root, "audit-2025.csv"), Workbook);
    }

    [Fact]
    public async Task Workbook_rows_keep_their_approver_and_row()
    {
        var (exit, output, error) = await ImportAsync();

        exit.ShouldBe(ExitCodes.Ok, error);
        var answer = JsonNode.Parse(output)!;
        answer["audit"]!.GetValue<string>().ShouldBe("EX-E8-2026");
        answer["imported"]!.AsArray().Select(r => (r!["row"]!.GetValue<int>(), r["requirement"]!.GetValue<string>())).ShouldBe([(2, "ISM-1807")]);
        answer["unmatched"]!.AsArray().Select(r => (r!["row"]!.GetValue<int>(), r["requirement"]!.GetValue<string>())).ShouldBe([(4, "ISM-9999")]);
        answer["already_assessed"]!.AsArray().Select(r => r!["requirement"]!.GetValue<string>()).ShouldBe(["ISM-1486"]);

        // The audit holds the row as the workbook records it: its result, and who approved it, when, in which row.
        var imported = (await AuditAsync()).Assessments.Single(a => Ref(a) == "ISM-1807");
        imported.Result.ShouldBe(ComplianceResult.Compliant);
        imported.Status.ShouldBe("done");
        imported.Observation.ShouldBe(
            "Asset discovery runs daily, and its history is kept. Evidence: Discovery tool run history export. Imported from audit-2025.csv, row 2, approved by Sam Example on 2026-05-01.");

        // Importing again changes nothing: every row is already there or already a draft.
        (exit, output, error) = await ImportAsync();
        exit.ShouldBe(ExitCodes.Ok, error);
        JsonNode.Parse(output)!["imported"]!.AsArray().ShouldBeEmpty();
        (await AuditAsync()).Assessments.Count(a => Ref(a) == "ISM-1807").ShouldBe(1);
    }

    [Fact]
    public async Task Row_without_an_approver_is_a_draft()
    {
        var (exit, output, error) = await ImportAsync();

        exit.ShouldBe(ExitCodes.Ok, error);
        JsonNode.Parse(output)!["drafts"]!.AsArray().Select(r => (r!["row"]!.GetValue<int>(), r["requirement"]!.GetValue<string>())).ShouldBe([(3, "ISM-1808")]);
        (await AuditAsync()).Assessments.ShouldNotContain(a => Ref(a) == "ISM-1808");
        File.Exists(Path.Combine(Repo, "onboarding", "drafts", "imports", "ex-e8-2026.json")).ShouldBeTrue();

        // Piped answers approve nothing; a person on a terminal approves the row, and is recorded as its approver, today.
        (exit, _, _) = await ApproveAsync("Sam Example\ny\n", terminal: false);
        exit.ShouldBe(ExitCodes.Usage);
        (exit, output, error) = await ApproveAsync("Alex Example\ny\n", terminal: true);
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("[imported row] EX-E8-2026 ISM-1808: partially_compliant. The scanner's database updates weekly.");
        error.ShouldContain("audit-2025.csv, row 3");
        output.ShouldContain("Approved 1 of 1.");
        var approved = (await AuditAsync()).Assessments.Single(a => Ref(a) == "ISM-1808");
        approved.Result.ShouldBe(ComplianceResult.PartiallyCompliant);
        approved.Observation.ShouldBe(
            "The scanner's database updates weekly. Evidence: Scanner configuration screenshot. Imported from audit-2025.csv, row 3, approved by Alex Example on 2026-11-23.");
        (_, output, _) = await ApproveAsync("", terminal: true);
        output.ShouldContain("Nothing is waiting for approval.");
    }

    [Fact]
    public async Task Columns_can_be_named_and_an_excel_workbook_is_read()
    {
        // The same two rows as an .xlsx with the person's own headings, mapped with --column.
        var workbook = Path.Combine(_root, "audit-2025.xlsx");
        XlsxFixture.Write(workbook,
        [
            ["Control", "Status", "Notes", "Signed off by", "Signed off"],
            ["ISM-1807", "Compliant", "Asset discovery runs daily.", "Sam Example", "2026-05-01"],
            ["ISM-1808", "Not assessed", "", "", ""],
        ]);

        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(
            [
                "scope", "import", workbook, "--repo", Repo, "--audit", "EX-E8-2026", "--json", "--column", "requirement=Control", "--column", "result=Status",
                "--column", "observation=Notes", "--column", "approved_by=Signed off by", "--column", "approved_on=Signed off",
            ],
            "", terminal: false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        JsonNode.Parse(output)!["imported"]!.AsArray().Count.ShouldBe(1);
        (await AuditAsync()).Assessments.Single(a => Ref(a) == "ISM-1807").Observation
            .ShouldBe("Asset discovery runs daily. Imported from audit-2025.xlsx, row 2, approved by Sam Example on 2026-05-01.");

        // A column the workbook does not have, or a result hound does not know, is a problem naming it; nothing is written.
        (exit, _, error) = await OnboardAgentCommandTests.RunAsync(
            ["scope", "import", workbook, "--repo", Repo, "--audit", "EX-E8-2026", "--json"], "", terminal: false, Path.Combine(_root, "home"), _ct);
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("has no column");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private Task<(int Exit, string Output, string Error)> ImportAsync() => OnboardAgentCommandTests.RunAsync(
        ["scope", "import", Path.Combine(_root, "audit-2025.csv"), "--repo", Repo, "--audit", "EX-E8-2026", "--json"], "", terminal: false, Path.Combine(_root, "home"), _ct);

    private Task<(int Exit, string Output, string Error)> ApproveAsync(string answers, bool terminal) =>
        OnboardAgentCommandTests.RunAsync(["approve", "--repo", Repo], answers, terminal, Path.Combine(_root, "home"), _ct);

    private static string Ref(HandAssessment assessment) => assessment.Requirement.Key.Value;

    private async Task<ScopeAudit> AuditAsync()
    {
        var file = Path.Combine(Repo, "scope", "audits", "ex-e8-2026.yaml");
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), _ct)), null, [new ScopeSource(file, await File.ReadAllTextAsync(file, _ct))]);
        parsed.Problems.ShouldBeEmpty();
        return parsed.Scope.Audits.Single();
    }
}
