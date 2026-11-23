using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>s09-25: docs/onboarding.md keeps up with every <c>hound onboard</c> option, its links work, and it has a tutorial on the fictional example API.</summary>
public sealed partial class OnboardingDocsTests
{
    private static readonly string Docs = Path.Combine(ExampleRepoTests.RepoRoot, "docs");

    private static readonly string Guide = File.ReadAllText(Path.Combine(Docs, "onboarding.md"));

    [Theory]
    [InlineData("onboard")]
    [InlineData("onboard config")]
    [InlineData("onboard environment")]
    [InlineData("onboard profile")]
    public void Every_onboard_option_is_documented(string command)
    {
        var options = Options(command.Split(' '));

        options.ShouldNotBeEmpty();
        options.Where(option => !Guide.Contains($"`{option}", StringComparison.Ordinal) && !Guide.Contains($" {option}", StringComparison.Ordinal))
            .ShouldBeEmpty($"hound {command} options missing from docs/onboarding.md");
    }

    [Fact]
    public void Every_relative_link_resolves()
    {
        var broken = Link().Matches(Guide).Select(m => m.Groups["target"].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith('#'))
            .Where(target => !Path.Exists(Path.GetFullPath(Path.Combine(Docs, target.Split('#')[0]))))
            .ToArray();

        broken.ShouldBeEmpty();
    }

    [Fact]
    public void Has_a_tutorial_onboarding_the_example_api()
    {
        Guide.ShouldContain("## Tutorial: onboard the example API");
        Guide.ShouldContain("examples/apis/example-asset-api.openapi.yaml");
        Guide.ShouldContain("--no-model");
    }

    /// <summary>The long option names that <c>hound &lt;command&gt; --help</c> lists, apart from help and version.</summary>
    private static string[] Options(string[] path)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        HoundCli.RunAsync([.. path, "--help"], output, error, configureLogging: null, new Dictionary<string, string?>(), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return [.. OptionLine().Matches(output.ToString()).Select(m => m.Groups["name"].Value).Where(name => name is not "--help" and not "--version").Distinct()];
    }

    [GeneratedRegex(@"^\s+(?:-\S+, )*(?<name>--[a-z][a-z-]*)", RegexOptions.Multiline)]
    private static partial Regex OptionLine();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();
}
