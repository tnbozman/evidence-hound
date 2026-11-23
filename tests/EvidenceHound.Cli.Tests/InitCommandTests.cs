namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: <c>hound init</c> scaffolds a new organisation's compliance repo that validates as written.</summary>
public sealed class InitCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-init-tests-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private string Repo => Path.Combine(_root, "compliance");

    [Fact]
    public async Task Yes_writes_a_repo_that_validates()
    {
        var exitCode = await Run("", "init", Repo, "--yes");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        foreach (var file in new[] { "hound.yaml", "scope/setup.yaml", "scope/controls.yaml", "mappings/essential-eight-2023.yaml", "frameworks/essential-eight-2023.json", ".gitignore", "README.md" })
        {
            File.Exists(Path.Combine(Repo, file)).ShouldBeTrue(file);
        }

        Directory.GetFiles(Path.Combine(Repo, "scope", "audits"), "*.yaml").ShouldHaveSingleItem();
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task Answers_on_stdin_set_the_organisation_code_level_and_owner()
    {
        // Organisation, code, CISO Assistant URL, time zone, maturity level, due date, owner.
        var exitCode = await Run("Example Two Pty Ltd\nEX2\n\n\n3\n2027-03-31\ngrc@example.com\n", "init", Repo);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var setup = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), TestContext.Current.CancellationToken);
        setup.ShouldContain("- name: Example Two Pty Ltd");
        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex2-e8-2027.yaml"), TestContext.Current.CancellationToken);
        audit.ShouldContain("ref_id: EX2-E8-2027");
        audit.ShouldContain("groups: [\"1\", \"2\", \"3\"]");
        audit.ShouldContain("due_date: \"2027-03-31\"");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "controls.yaml"), TestContext.Current.CancellationToken)).ShouldContain("owner: grc@example.com");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), TestContext.Current.CancellationToken)).ShouldContain("compliance_assessment: EX2-E8-2027");
        _error.ToString().ShouldContain("Organisation name [Example Pty Ltd]");
        _error.ToString().ShouldContain("Compliance owner's email, as a CISO Assistant user");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task A_pasted_control_character_is_dropped_from_an_answer()
    {
        var exitCode = await Run("Example Two Pty Ltd\nEX2\n\n\n2\n2027-03-31\n\u0016grc@example.com\n", "init", Repo);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var controls = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "controls.yaml"), TestContext.Current.CancellationToken);
        controls.ShouldContain("owner: grc@example.com");
        controls.ShouldNotContain("\u0016");
    }

    [Fact]
    public async Task Folder_that_is_not_empty_is_refused()
    {
        Directory.CreateDirectory(Repo);
        var existing = Path.Combine(Repo, "notes.txt");
        await File.WriteAllTextAsync(existing, "keep me", TestContext.Current.CancellationToken);

        var exitCode = await Run("", "init", Repo, "--yes");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("not empty");
        Directory.GetFileSystemEntries(Repo).ShouldBe([existing]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private async Task ShouldValidateAsync()
    {
        var config = Path.Combine(Repo, "hound.yaml");
        (await Run("", "config", "validate", config)).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
        (await Run("", "scope", "validate", Path.Combine(Repo, "scope"), "--config", config)).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
    }

    private Task<int> Run(string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, configureLogging: null, TestContext.Current.CancellationToken);
}
