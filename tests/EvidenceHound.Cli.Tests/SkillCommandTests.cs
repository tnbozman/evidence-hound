namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound skill</c>: write, add, validate and list the organisation's skills beside the defaults.</summary>
public sealed class SkillCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-skill-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_then_add_then_list_shows_it_beside_the_defaults()
    {
        var draft = Path.Combine(_root, "drafts");
        var skills = Path.Combine(_root, "skills");

        (await RunAsync("skill", "new", "example-check", "--skills", draft)).Exit.ShouldBe(0);
        var written = Path.Combine(draft, "example-check.md");
        (await File.ReadAllTextAsync(written, _ct)).ShouldStartWith("---\nname: example-check\n");

        var (addExit, _, addError) = await RunAsync("skill", "add", written, "--skills", skills);
        addExit.ShouldBe(0, addError);
        File.Exists(Path.Combine(skills, "example-check.md")).ShouldBeTrue();

        var (listExit, list, _) = await RunAsync("skill", "list", "--skills", skills);
        listExit.ShouldBe(0);
        list.ShouldContain("| example-check | organisation | 0.1.0 |");
        list.ShouldContain("| applicability | default | ");
    }

    [Fact]
    public async Task Add_refuses_an_invalid_skill_and_copies_nothing()
    {
        var bad = Path.Combine(_root, "bad.md");
        await File.WriteAllTextAsync(bad, "---\nname: bad\napplies_to: [deploy]\n---\nBody.\n", _ct);

        var (exit, _, error) = await RunAsync("skill", "add", bad, "--skills", Path.Combine(_root, "skills"));

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("'deploy' is not an onboarding step");
        Directory.Exists(Path.Combine(_root, "skills")).ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<(int Exit, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(args, output, error, cancellationToken: _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
