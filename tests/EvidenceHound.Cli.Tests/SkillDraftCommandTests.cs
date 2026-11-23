namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound skill draft</c> writes organisation skills into the organisation's skills folder, and nowhere else.</summary>
public sealed class SkillDraftCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-skill-draft-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SkillDraftCommandTests() => TestFiles.CopyExampleRepo(Repo);

    private string Repo => Path.Combine(_root, "repo");

    private string Skills => Path.Combine(_root, "skills");

    [Fact]
    public async Task Writes_only_to_the_organisation_skills_folder()
    {
        var before = Snapshot(Repo);

        var (exit, output, error) = await RunAsync("skill", "draft", "--skills", Skills);

        exit.ShouldBe(ExitCodes.Ok, error);
        Directory.GetFiles(Skills).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["ai-landscape.md", "it-landscape.md", "organisation-profile.md", "risk-appetite.md"]);
        Snapshot(Repo).ShouldBe(before);
        output.ShouldContain("review and commit");

        // Run again: a skill that is already there is the organisation's own, and is kept as it is.
        var own = (await File.ReadAllTextAsync(Path.Combine(Skills, "risk-appetite.md"), _ct)).Replace("_Answer here._", "We accept no unsupported software.", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(Skills, "risk-appetite.md"), own, _ct);
        File.Delete(Path.Combine(Skills, "ai-landscape.md"));
        (exit, output, error) = await RunAsync("skill", "draft", "--skills", Skills);
        exit.ShouldBe(ExitCodes.Ok, error);
        (await File.ReadAllTextAsync(Path.Combine(Skills, "risk-appetite.md"), _ct)).ShouldBe(own);
        output.ShouldContain("Wrote ai-landscape (version 1)");
        output.ShouldContain("Kept organisation-profile, risk-appetite, it-landscape");
    }

    [Fact]
    public async Task Templates_are_valid_skills_with_their_questions_as_headings()
    {
        (await RunAsync("skill", "draft", "--skills", Skills)).Exit.ShouldBe(ExitCodes.Ok);

        var text = await File.ReadAllTextAsync(Path.Combine(Skills, "ai-landscape.md"), _ct);
        text.ShouldStartWith("---\nname: ai-landscape\n");
        text.ShouldContain("\n## ");
        var (exit, output, error) = await RunAsync("skill", "validate", "--skills", Skills);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("skills valid (4 from");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllText, StringComparer.Ordinal);

    private async Task<(int Exit, string Output, string Error)> RunAsync(params string[] args)
    {
        using var reader = new StringReader("");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home") };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
