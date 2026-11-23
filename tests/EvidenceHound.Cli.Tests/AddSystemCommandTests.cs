namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: <c>hound add system</c> adds a system to a domain and to audits, keeping the scope files as they were.</summary>
public sealed class AddSystemCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-add-system-tests-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private string Repo => Path.Combine(_root, "repo");

    private string Scope => Path.Combine(Repo, "scope");

    private string SetupFile => Path.Combine(Scope, "setup.yaml");

    private string AuditFile => Path.Combine(Scope, "audits", "ex-e8-2026.yaml");

    [Fact]
    public async Task Adds_the_system_to_the_domain_and_the_named_audit_keeping_comments()
    {
        TestFiles.CopyExampleRepo(Repo);
        var before = await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken);

        // Name, ref ID, type, owner, description, parents.
        var exitCode = await Run("Example firewall\n\n\nit@example.com\nThe office firewall.\nExample 365\n", "add", "system", "--scope", Scope, "--audit", "EX-E8-2026");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var setup = await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken);
        setup.ShouldContain("""
                  - name: Example firewall
                    ref_id: SYS-EXAMPLE-FIREWALL
                    type: supporting
                    description: The office firewall.
                    owner: it@example.com
                    parents: [Example 365]
            """.ReplaceLineEndings("\n"));
        setup.Split('\n').Where(line => line.TrimStart().StartsWith('#')).ShouldBe(before.Split('\n').Where(line => line.TrimStart().StartsWith('#')));
        setup.Length.ShouldBeGreaterThan(before.Length);
        (await File.ReadAllTextAsync(AuditFile, TestContext.Current.CancellationToken)).ShouldContain("systems: [Example 365, Example laptops, Example backup service, Example firewall]");
        _error.ToString().ShouldContain("System name");
        _error.ToString().ShouldContain("Owner's email, as a CISO Assistant user");
        (await Run("", "scope", "validate", Scope, "--config", Path.Combine(Repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
    }

    [Fact]
    public async Task Duplicate_name_is_refused_and_nothing_is_written()
    {
        TestFiles.CopyExampleRepo(Repo);
        var before = await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken);
        var audit = await File.ReadAllTextAsync(AuditFile, TestContext.Current.CancellationToken);

        var exitCode = await Run("", "add", "system", "--scope", Scope, "--yes", "--name", "Example 365", "--owner", "it@example.com", "--audit", "EX-E8-2026");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("Example 365");
        (await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken)).ShouldBe(before);
        (await File.ReadAllTextAsync(AuditFile, TestContext.Current.CancellationToken)).ShouldBe(audit);
    }

    [Fact]
    public async Task Ref_id_without_its_prefix_gets_it()
    {
        TestFiles.CopyExampleRepo(Repo);

        var exitCode = await Run("", "add", "system", "--scope", Scope, "--yes", "--name", "Example firewall", "--ref-id", "firewall", "--owner", "it@example.com");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        (await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken)).ShouldContain("ref_id: SYS-FIREWALL\n");
    }

    [Fact]
    public async Task Ref_id_prompt_shows_the_prefix_and_asks_again_until_it_fits()
    {
        TestFiles.CopyExampleRepo(Repo);

        // Name, ref ID (refused, then given), type, owner, description, parents.
        var exitCode = await Run("Example firewall\noffice firewall\noffice-fw\n\nit@example.com\nThe office firewall.\n\n", "add", "system", "--scope", Scope);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _error.ToString().ShouldContain("System ref_id [EXAMPLE-FIREWALL]: SYS-");
        _error.ToString().ShouldContain("upper case letters, digits and hyphens");
        (await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken)).ShouldContain("ref_id: SYS-OFFICE-FW\n");
    }

    [Fact]
    public async Task Ref_id_option_that_does_not_fit_is_refused()
    {
        TestFiles.CopyExampleRepo(Repo);
        var before = await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken);

        var exitCode = await Run("", "add", "system", "--scope", Scope, "--yes", "--name", "Example firewall", "--ref-id", "office firewall", "--owner", "it@example.com");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("SYS-");
        (await File.ReadAllTextAsync(SetupFile, TestContext.Current.CancellationToken)).ShouldBe(before);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private Task<int> Run(string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, configureLogging: null, TestContext.Current.CancellationToken);
}
