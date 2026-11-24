using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>s11-01: <c>hound programme list</c> shows each programme's framework, groups, audit of record and requirement counts, from the snapshot only.</summary>
public sealed class ProgrammeCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-programme-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ProgrammeCommandTests() => TestFiles.CopyExampleRepo(Repo);

    [Fact]
    public async Task Lists_the_essential_eight_programme_with_its_strategies()
    {
        var (exit, output, error) = await RunAsync(["programme", "list", "--scope", Scope, "--json"]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var programme = JsonNode.Parse(output)!["programmes"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "essential-eight")!;
        (programme["framework"]!.GetValue<string>(), programme["audit"]!.GetValue<string>(), programme["version"]!.GetValue<int>())
            .ShouldBe(("urn:intuitem:risk:framework:essential-eight-2023", "EX-E8-2026", 3));
        var groups = programme["groups"]!.AsArray();
        groups.Select(g => (g!["ref_id"]!.GetValue<string>(), g["requirements"]!.GetValue<int>())).ShouldBe([("1", 48), ("2", 107)]);
        groups.ShouldAllBe(g => g!["parents"]!.AsArray().Count == 8);
        groups[0]!["parents"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "mfa")!["requirements"]!.GetValue<int>().ShouldBe(7);

        (exit, output, error) = await RunAsync(["programme", "list", "--scope", Scope]);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("essential-eight");
        output.ShouldContain("EX-E8-2026");
        output.ShouldContain("| mfa | 7 | 19 |");
    }

    [Fact]
    public async Task A_scope_problem_is_reported_and_nothing_is_listed()
    {
        var setup = Path.Combine(Scope, "setup.yaml");
        await File.WriteAllTextAsync(setup, (await File.ReadAllTextAsync(setup, _ct)).Replace("audit: EX-E8-2026", "audit: EX-MISSING", StringComparison.Ordinal), _ct);

        var (exit, _, error) = await RunAsync(["programme", "list", "--scope", Scope]);

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("EX-MISSING");
    }

    [Fact]
    public async Task List_shows_the_register_counts()
    {
        var setup = Path.Combine(Scope, "setup.yaml");
        await File.WriteAllTextAsync(
            setup,
            (await File.ReadAllTextAsync(setup, _ct)).Replace("    audit: EX-E8-2026\n", "    audit: EX-E8-2026\n    register:\n      of: systems\n      by: [type]\n", StringComparison.Ordinal),
            _ct);

        var (exit, output, error) = await RunAsync(["programme", "list", "--scope", Scope]);

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Register: 5 systems. type: primary 1, supporting 4.");
        (exit, output, error) = await RunAsync(["programme", "list", "--scope", Scope, "--json"]);
        exit.ShouldBe(ExitCodes.Ok, error);
        var register = JsonNode.Parse(output)!["programmes"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "essential-eight")!["register"]!;
        (register["of"]!.GetValue<string>(), register["total"]!.GetValue<int>()).ShouldBe(("systems", 5));
        register["counts"]!.AsArray().Select(c => (c!["field"]!.GetValue<string>(), c["value"]!.GetValue<string>(), c["count"]!.GetValue<int>()))
            .ShouldBe([("type", "primary", 1), ("type", "supporting", 4)]);
    }

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args) => OnboardAgentCommandTests.RunAsync(args, "", terminal: false, _root, _ct);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private string Scope => Path.Combine(Repo, "scope");
}
