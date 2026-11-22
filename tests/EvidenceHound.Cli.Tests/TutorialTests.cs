using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s07-06: the first deployment tutorial cannot drift from the code. Every path it names exists in the template compliance repo or in
/// Evidence Hound, and every <c>hound</c> command it runs (directly or as <c>docker compose run --rm runner …</c>) exists with the options it uses.
/// </summary>
public sealed partial class TutorialTests
{
    private static string Tutorial => Path.Combine(ExampleRepoTests.RepoRoot, "docs", "tutorials", "first-deployment.md");

    [Fact]
    public void Tutorial_walks_through_the_steps_from_an_empty_vm_to_a_scheduled_run()
    {
        var text = File.ReadAllText(Tutorial);

        foreach (var expected in new[]
        {
            "private", "branch protection", "push protection", "deploy/deploy.env", "compose.override.yml", "scope/access.yaml", "scope/settings.yaml",
            "./manage.sh createsuperuser", "runner-token.sh", "docs/collectors/m365.md", "m365_client_certificate", "enforce_mfa",
            "scope validate", "scope plan", "scope apply", "docker compose up -d runner", "status", "pull request",
        })
        {
            text.ShouldContain(expected, Case.Sensitive, $"the tutorial mentions {expected}");
        }
    }

    [Fact]
    public void Every_path_and_command_in_tutorial_exists()
    {
        var text = File.ReadAllText(Tutorial);

        var missingPaths = InlineCode().Matches(text).Select(m => m.Groups["code"].Value).Concat(CodeBlocks(text).SelectMany(Words))
            .Where(IsRepoPath)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !Exists(path))
            .ToArray();
        missingPaths.ShouldBeEmpty();

        var commands = CodeBlocks(text).SelectMany(block => block.Split('\n')).Select(Command).OfType<string[]>().ToArray();
        commands.ShouldNotBeEmpty();
        commands.Select(Unknown).OfType<string>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("hound scope bogus scope/")]
    [InlineData("hound scope plan scope/ --bogus")]
    public void A_command_or_option_that_does_not_exist_is_caught(string line)
    {
        Unknown(Command(line)!).ShouldNotBeNull();
    }

    /// <summary>A path into the template repo or Evidence Hound: relative, with a folder and no placeholders.</summary>
    private static bool IsRepoPath(string word) =>
        RepoPath().IsMatch(word) && !word.Contains("://", StringComparison.Ordinal);

    private static bool Exists(string path)
    {
        var relative = path.TrimStart('.', '/');
        return new[] { Path.Combine(ExampleRepoTests.RepoRoot, "examples", "compliance-repo"), ExampleRepoTests.RepoRoot, Path.Combine(ExampleRepoTests.RepoRoot, "deploy", "compose") }
            .Any(root => Path.Exists(Path.Combine(root, relative.TrimEnd('/'))));
    }

    /// <summary>The words of a <c>hound</c> command line after <c>hound</c> (or the runner service, whose entrypoint is <c>hound</c>).</summary>
    private static string[]? Command(string line)
    {
        var match = HoundCommand().Match(line);
        return match.Success ? match.Groups["args"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
    }

    /// <summary>Why a command does not exist, or null when its subcommand and every option it uses do.</summary>
    private static string? Unknown(string[] args)
    {
        var path = args.TakeWhile(a => SubcommandWord().IsMatch(a)).ToArray();
        using var output = new StringWriter();
        using var error = new StringWriter();
        HoundCli.RunAsync([.. path, "--help"], output, error, configureLogging: null, new Dictionary<string, string?>(), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        var help = output.ToString();
        var usage = $"hound {string.Join(' ', path)}";
        if (!help.Contains($"\n  {usage} ", StringComparison.Ordinal) && !help.Contains($"\n  {usage}\n", StringComparison.Ordinal))
        {
            return $"'{usage}' is not a command";
        }

        var options = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).Select(a => a.Split('=')[0]).Where(o => !help.Contains(o, StringComparison.Ordinal)).ToArray();
        return options.Length == 0 ? null : $"'{usage}' has no option {string.Join(", ", options)}";
    }

    private static IEnumerable<string> CodeBlocks(string markdown) => CodeBlock().Matches(markdown).Select(m => m.Groups["body"].Value);

    private static IEnumerable<string> Words(string block) => block.Split([' ', '\n', '\t', '"', '\'', '(', ')', '='], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"(?<!`)`(?<code>[^`\n]+)`(?!`)")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^```[a-z]*\n(?<body>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"(?:^|\s)(?:hound|docker compose run --rm runner)\s+(?<args>[^|>&;#\n]*)")]
    private static partial Regex HoundCommand();

    [GeneratedRegex(@"^[a-z][a-z-]*$")]
    private static partial Regex SubcommandWord();

    /// <summary>Relative paths such as <c>scope/access.yaml</c>, <c>deploy/</c> or <c>./manage.sh</c>; not <c>/run/secrets/…</c>, <c>~/…</c> or <c>&lt;name&gt;</c>.</summary>
    [GeneratedRegex(@"^(?:\./)?(?:[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]*$|^\./[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPath();
}
