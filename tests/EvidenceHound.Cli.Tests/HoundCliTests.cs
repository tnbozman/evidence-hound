using System.Reflection;

namespace EvidenceHound.Cli.Tests;

/// <summary>s09-40: hound has no language model of its own. A coding assistant set up with <c>hound agent setup</c> is the one way to get model help.</summary>
public sealed class HoundCliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-cli-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Onboard_config_points_to_agent_setup()
    {
        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["onboard", "config", "--provider", "anthropic"], "example-key\n", terminal: false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("hound no longer has a built-in model");
        error.ShouldContain("hound agent setup");
        output.ShouldBeEmpty();
        Directory.Exists(Path.Combine(_root, "home")).ShouldBeFalse("no key file is written");
    }

    [Theory]
    [InlineData("--no-model")]
    [InlineData("--show-requests")]
    public async Task Options_that_only_served_the_model_point_to_agent_setup(string option)
    {
        // The option is still taken, so an older script keeps working, but it does nothing and says where model help now comes from.
        const string diff = "--- a/mappings/example.yaml\n+++ b/mappings/example.yaml\n@@ -1,3 +1,2 @@\n requirements:\n-  - ref: EX-4\n   - ref: EX-5\n";
        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["explain", "--repo", _root, option], diff, terminal: false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("1 file changed");
        error.ShouldContain($"{option} does nothing now: hound has no built-in model");
        error.ShouldContain("hound agent setup");
    }

    [Fact]
    public void No_assembly_of_hound_references_a_model_sdk()
    {
        var folder = Path.GetDirectoryName(typeof(HoundCli).Assembly.Location)!;
        Directory.GetFiles(folder, "*.dll").Select(Path.GetFileName).ShouldNotContain(f => f!.StartsWith("Anthropic", StringComparison.Ordinal) || f.StartsWith("OpenAI", StringComparison.Ordinal));
        foreach (var name in new[] { "hound", "EvidenceHound.Onboarding" })
        {
            Assembly.Load(name).GetReferencedAssemblies().Select(r => r.Name).ShouldNotContain(r => r == "Anthropic" || r == "OpenAI", name);
        }

        var help = File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "src", "EvidenceHound.Cli", "OnboardCommands.cs"));
        help.ShouldNotContain("api_key");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
