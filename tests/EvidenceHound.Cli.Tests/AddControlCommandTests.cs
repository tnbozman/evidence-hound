namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: <c>hound add control</c> declares an applied control and links it to an audit's requirements.</summary>
public sealed class AddControlCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-add-control-tests-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private string Repo => Path.Combine(_root, "repo");

    private string Scope => Path.Combine(Repo, "scope");

    [Fact]
    public async Task Declares_the_control_and_links_it_to_the_audit_requirements()
    {
        TestFiles.CopyExampleRepo(Repo);

        // Ref ID, name, owner, category, systems, requirements in the audit.
        var exitCode = await Run(
            "AC-FIREWALL\nFirewall rules reviewed quarterly\nit@example.com\n\nExample 365\nISM-1173, ISM-1504\n", "add", "control", "--scope", Scope, "--audit", "EX-E8-2026");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        (await File.ReadAllTextAsync(Path.Combine(Scope, "controls.yaml"), TestContext.Current.CancellationToken)).ShouldContain("""
              - ref_id: AC-FIREWALL
                name: Firewall rules reviewed quarterly
                domain: Example Pty Ltd
                owner: it@example.com
                status: to_do
                category: technical
                systems: [Example 365]
            """.ReplaceLineEndings("\n"));
        (await File.ReadAllTextAsync(Path.Combine(Scope, "audits", "ex-e8-2026.yaml"), TestContext.Current.CancellationToken)).ShouldContain("""
              - ref_id: AC-FIREWALL
                requirements: [ISM-1173, ISM-1504]
            """.ReplaceLineEndings("\n"));
        (await Run("", "scope", "validate", Scope, "--config", Path.Combine(Repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
    }

    [Fact]
    public async Task Requirement_the_framework_does_not_have_is_refused_and_nothing_is_written()
    {
        TestFiles.CopyExampleRepo(Repo);
        var controls = await File.ReadAllTextAsync(Path.Combine(Scope, "controls.yaml"), TestContext.Current.CancellationToken);

        var exitCode = await Run(
            "", "add", "control", "--scope", Scope, "--yes", "--ref-id", "AC-FIREWALL", "--name", "Firewall rules", "--owner", "it@example.com", "--audit", "EX-E8-2026", "--requirements", "ISM-9999");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("ISM-9999");
        (await File.ReadAllTextAsync(Path.Combine(Scope, "controls.yaml"), TestContext.Current.CancellationToken)).ShouldBe(controls);
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
