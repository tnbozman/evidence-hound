using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>s15-09: <c>hound irp build</c> writes the example's incident response plan from the repo, gaps first.</summary>
public sealed class IrpCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-irp-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public IrpCommandTests() => TestFiles.CopyExampleRepo(Repo);

    [Fact]
    public async Task The_example_plan_has_no_gaps_and_lists_its_reports()
    {
        var (exit, output, error) = await RunAsync(["irp", "build", "--programme", "essential-eight", "--scope", Scope, "--json", "--strict"]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var plan = JsonNode.Parse(output)!;
        plan["gaps"]!.AsArray().ShouldBeEmpty();
        plan["reports"]!.AsArray().Select(r => r!["name"]!["value"]!.GetValue<string>()).ShouldContain("DFARS 252.204-7012 cyber incident report");

        (exit, output, error) = await RunAsync(["irp", "build", "--programme", "essential-eight", "--scope", Scope]);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("# Incident response plan");
    }

    [Fact]
    public async Task Strict_build_with_gaps_exits_non_zero()
    {
        File.Delete(Path.Combine(Repo, "irp", "lessons-learned.md"));

        var (exit, output, error) = await RunAsync(["irp", "build", "--programme", "essential-eight", "--scope", Scope, "--strict"]);

        exit.ShouldBe(ExitCodes.SspGaps, error);
        output.ShouldContain("lessons_learned");
        (exit, _, error) = await RunAsync(["irp", "build", "--programme", "essential-eight", "--scope", Scope]);
        exit.ShouldBe(ExitCodes.Ok, error);
    }

    [Fact]
    public async Task A_programme_the_plan_does_not_name_is_refused()
    {
        var (exit, _, error) = await RunAsync(["irp", "build", "--programme", "disp", "--scope", Scope]);

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("irp.programme_unknown");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args) => OnboardAgentCommandTests.RunAsync(args, "", terminal: false, _root, _ct);

    private string Repo => Path.Combine(_root, "repo");

    private string Scope => Path.Combine(Repo, "scope");
}
