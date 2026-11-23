using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-41: <c>hound audit draft</c> drafts an audit for named systems with every answer as an option. The draft is kept outside the scope
/// until a person approves it with <c>hound approve</c>, which moves it into <c>scope/audits/</c>.
/// </summary>
public sealed class AuditDraftCommandTests : IDisposable
{
    internal const string Framework = "urn:intuitem:risk:framework:essential-eight-2023";

    private readonly string _root = Directory.CreateTempSubdirectory("hound-audit-draft-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public AuditDraftCommandTests() => TestFiles.CopyExampleRepo(Repo);

    [Fact]
    public async Task Audit_is_drafted_for_the_named_systems()
    {
        var (exit, output, error) = await DraftAsync("EX-E8-2027", "--system", "Example 365", "--system", "Example laptops");

        exit.ShouldBe(ExitCodes.Ok, error);
        var answer = JsonNode.Parse(output)!;
        answer["file"]!.GetValue<string>().ShouldBe("onboarding/drafts/audits/ex-e8-2027.yaml");
        answer["approved"]!.GetValue<bool>().ShouldBeFalse();
        var draft = await File.ReadAllTextAsync(Path.Combine(Repo, "onboarding", "drafts", "audits", "ex-e8-2027.yaml"), _ct);
        draft.ShouldContain("ref_id: \"EX-E8-2027\"");
        draft.ShouldContain($"framework: \"{Framework}\"");
        draft.ShouldContain("groups: [\"1\", \"2\"]");
        draft.ShouldContain("systems: [\"Example 365\", \"Example laptops\"]");
        draft.ShouldContain("due_date: \"2027-06-30\"");
        File.Exists(Scoped).ShouldBeFalse();

        // What the scope does not hold is refused, and nothing is written.
        (exit, _, error) = await DraftAsync("EX-E8-2028", "--system", "Example mainframe");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("audit.system_unknown");
        (exit, _, error) = await DraftAsync("EX-E8-2028", "--system", "Example 365", "--perimeter", "Nowhere");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("audit.perimeter_unknown");
        (exit, _, error) = await DraftAsync("EX-E8-2026", "--system", "Example 365");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("audit.exists");

        // An agent cannot approve it; a person on a terminal moves it into the scope, where it validates.
        (exit, _, _) = await ApproveAsync("Sam Example\ny\n", terminal: false);
        exit.ShouldBe(ExitCodes.Usage);
        (exit, output, error) = await ApproveAsync("Sam Example\ny\n", terminal: true);
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("[audit] EX-E8-2027 Essential Eight ML2 2027");
        error.ShouldContain("Example 365, Example laptops");
        output.ShouldContain("Approved 1 of 1.");
        var audit = await File.ReadAllTextAsync(Scoped, _ct);
        audit.ShouldContain("# Drafted by an agent with hound audit draft on 2026-11-23. Approved by Sam Example on 2026-11-23.");
        File.Exists(Path.Combine(Repo, "onboarding", "drafts", "audits", "ex-e8-2027.yaml")).ShouldBeFalse();
        (exit, output, error) = await OnboardAgentCommandTests.RunAsync(
            ["scope", "validate", Path.Combine(Repo, "scope"), "--config", Path.Combine(Repo, "hound.yaml")], "", terminal: false, Path.Combine(_root, "home"), _ct);
        exit.ShouldBe(ExitCodes.Ok, output + error);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private string Scoped => Path.Combine(Repo, "scope", "audits", "ex-e8-2027.yaml");

    private Task<(int Exit, string Output, string Error)> ApproveAsync(string answers, bool terminal) =>
        OnboardAgentCommandTests.RunAsync(["approve", "--repo", Repo], answers, terminal, Path.Combine(_root, "home"), _ct);

    private Task<(int Exit, string Output, string Error)> DraftAsync(string refId, params string[] more)
    {
        string[] place = more.Contains("--perimeter") ? [] : ["--perimeter", "Corporate IT"];
        return OnboardAgentCommandTests.RunAsync(
            [
                "audit", "draft", "--repo", Repo, "--ref", refId, "--name", "Essential Eight ML2 2027", "--framework", Framework, "--group", "1", "--group", "2",
                "--domain", "Example Pty Ltd", "--due", "2027-06-30", "--json", .. place, .. more,
            ],
            "", terminal: false, Path.Combine(_root, "home"), _ct);
    }
}
