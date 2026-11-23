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

        var (exit, output, error) = await RunAsync("skill", "draft", "--repo", Repo, "--skills", Skills, "--no-model");

        exit.ShouldBe(ExitCodes.Ok, error);
        Directory.GetFiles(Skills).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["ai-landscape.md", "it-landscape.md", "organisation-profile.md", "risk-appetite.md"]);
        Snapshot(Repo).ShouldBe(before);
        output.ShouldContain("Review, edit and commit");

        // Drafted again: each skill's version goes up, with the change shown.
        (exit, _, error) = await RunAsync("skill", "draft", "--repo", Repo, "--skills", Skills, "--no-model");
        exit.ShouldBe(ExitCodes.Ok, error);
        (await File.ReadAllTextAsync(Path.Combine(Skills, "risk-appetite.md"), _ct)).ShouldContain("\nversion: 2\n");
        error.ShouldContain("-version: 1");
    }

    [Fact]
    public async Task No_model_writes_templates()
    {
        (await RunAsync("skill", "draft", "--repo", Repo, "--skills", Skills, "--no-model")).Exit.ShouldBe(ExitCodes.Ok);

        var text = await File.ReadAllTextAsync(Path.Combine(Skills, "ai-landscape.md"), _ct);
        text.ShouldStartWith("---\nname: ai-landscape\n");
        text.ShouldContain("\n## ");
        var (exit, output, error) = await RunAsync("skill", "validate", "--skills", Skills);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("0 warnings");

        // A skill holding organisation data is refused.
        await File.WriteAllTextAsync(Path.Combine(Skills, "ai-landscape.md"), text.Replace("\n## ", "\nAdmins use 10.20.30.40.\n\n## ", StringComparison.Ordinal), _ct);
        (exit, output, error) = await RunAsync("skill", "validate", "--skills", Skills);
        exit.ShouldBe(ExitCodes.Configuration);
        (output + error).ShouldContain("skill.sensitive_content");
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
