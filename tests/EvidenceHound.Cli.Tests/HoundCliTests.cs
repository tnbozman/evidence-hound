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
    [InlineData("explain", "--no-model")]
    [InlineData("explain", "--show-requests")]
    [InlineData("explain", "--framework")]
    [InlineData("onboard environment", "--no-model")]
    [InlineData("onboard profile web", "--show-requests")]
    [InlineData("skill draft", "--show-requests")]
    [InlineData("skill validate", "--deny-terms")]
    public async Task Options_that_only_served_the_removed_model_are_refused(string command, string option)
    {
        // s19-02: removed in release 3 after being hidden and inert since s09; an unknown option is a usage error like any other.
        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync([.. command.Split(' '), option], "", terminal: false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Usage, error);
        error.ShouldContain(option);
        error.ShouldNotContain("does nothing now");
        output.ShouldBeEmpty();
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
