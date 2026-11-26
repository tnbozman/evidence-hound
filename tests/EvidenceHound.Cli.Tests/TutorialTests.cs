using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s07-06: the first deployment tutorial cannot drift from the code. Every path it names exists in the template compliance repo or in
/// Evidence Hound, and every <c>hound</c> command it runs (directly or as <c>docker compose run --rm runner …</c>) exists with the options it uses.
/// </summary>
public sealed partial class TutorialTests
{
    private static string Tutorial => Path.Combine(ExampleRepoTests.RepoRoot, "docs", "tutorials", "first-deployment.md");

    private static string OnboardingTutorial => Path.Combine(ExampleRepoTests.RepoRoot, "docs", "tutorials", "onboarding-tutorial.md");

    /// <summary>What the reviewer types at each prompt of the onboarding tutorial, one file per system and run.</summary>
    private static string OnboardingAnswers => Path.Combine(ExampleRepoTests.Tutorial, "answers");

    /// <summary>The day the committed tutorial files were written, which onboarding records in them.</summary>
    internal const string TutorialDate = "2026-11-23";

    private static readonly (string Id, string Slug, string Supplier)[] TutorialSystems =
    [
        ("GRC-PLATFORM", "grc-platform", "sup-oracle.yaml"),
        ("OFFICE-FIREWALL", "office-firewall", "sup-sophos.yaml"),
    ];

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
    public void Every_path_and_command_in_tutorial_exists() => PathsAndCommandsExist(File.ReadAllText(Tutorial));

    /// <summary>s09-32: the onboarding tutorial runs only commands and options that exist, and names only files that exist.</summary>
    [Fact]
    public void Onboarding_tutorial_names_only_files_that_exist() => PathsAndCommandsExist(File.ReadAllText(OnboardingTutorial));

    /// <summary>s09-32: addresses in the tutorial and its answer files come from the documentation ranges, so none can be anyone's real network.</summary>
    [Fact]
    public void Onboarding_tutorial_uses_only_documentation_addresses()
    {
        var texts = Directory.GetFiles(OnboardingAnswers).Append(OnboardingTutorial).Select(File.ReadAllText).ToArray();

        var addresses = texts.SelectMany(t => IPv4().Matches(t).Select(m => m.Value)).Distinct(StringComparer.Ordinal).ToArray();
        addresses.ShouldNotBeEmpty("the tutorial shows the network path with addresses");
        addresses.Where(a => !Documentation(a)).ShouldBeEmpty();
        texts.SelectMany(t => Email().Matches(t).Select(m => m.Value)).Where(e => !e.EndsWith("@example.com", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("192.0.2.10", true)]
    [InlineData("198.51.100.0/24", true)]
    [InlineData("203.0.113.254", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("192.0.3.1", false)]
    [InlineData("8.8.8.8", false)]
    public void Only_the_documentation_ranges_count_as_documentation_addresses(string address, bool documentation)
    {
        Documentation(address).ShouldBe(documentation);
    }

    /// <summary>s09-32: the tutorial covers the deployment, its network path and the controls on each part, and onboarding.md points to it.</summary>
    [Fact]
    public void Onboarding_tutorial_covers_the_stack_and_its_network_path()
    {
        var text = File.ReadAllText(OnboardingTutorial);

        foreach (var expected in new[]
        {
            "```mermaid", "Site-to-Site VPN", "IPSec", "Bastion", "no public IP", "Caddy", "PostgreSQL", "backups", "/run/secrets", "schedule",
            "VM.Standard.E5.Flex", "per month", "On another cloud", "sophos-central", "terraform-drift", "SUP-CISO-ASSISTANT", "SUP-EVIDENCE-HOUND",
            "SUP-ORACLE", "SUP-SOPHOS", "scope validate", "scope plan", "scope apply", "Example 365",
        })
        {
            text.ShouldContain(expected, Case.Sensitive, $"the tutorial mentions {expected}");
        }

        File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "onboarding.md")).ShouldContain("(tutorials/onboarding-tutorial.md)");
    }

    /// <summary>
    /// s09-32 functional check: typing the tutorial's answers into <c>hound onboard</c>, without a model, on a copy of the template with the hand-written
    /// suppliers, writes the same audits, systems and onboarding records as <c>examples/onboarding-tutorial</c> holds. Between the two runs of each system the reviewed
    /// supplier file stands in for the person's review, as in the tutorial.
    /// </summary>
    [Fact]
    public async Task Onboarding_tutorial_answers_reproduce_the_committed_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = ExampleRepoTests.Tutorial;
        var repo = Directory.CreateTempSubdirectory("hound-tutorial").FullName;
        try
        {
            // The tutorial starts from the template, with the open-source suppliers written by hand.
            ExampleRepoTests.CopyFolder(ExampleRepoTests.Repo, repo);
            Directory.CreateDirectory(Path.Combine(repo, "scope", "third-parties"));
            foreach (var project in new[] { "sup-ciso-assistant.yaml", "sup-evidence-hound.yaml" })
            {
                File.Copy(Path.Combine(expected, "scope", "third-parties", project), Path.Combine(repo, "scope", "third-parties", project));
            }

            foreach (var system in TutorialSystems)
            {
                string[] args = ["onboard", system.Id, "--repo", repo, "--group", "1", "--group", "2"];
                (await Onboard(args, Path.Combine(OnboardingAnswers, $"{system.Slug}.supplier.txt"), ct)).ShouldBe(ExitCodes.Ok);
                File.Exists(Path.Combine(repo, "scope", "third-parties", system.Supplier)).ShouldBeTrue($"onboarding {system.Id} drafts its supplier");
                File.Copy(Path.Combine(expected, "scope", "third-parties", system.Supplier), Path.Combine(repo, "scope", "third-parties", system.Supplier), overwrite: true);
                (await Onboard(args, Path.Combine(OnboardingAnswers, $"{system.Slug}.decisions.txt"), ct)).ShouldBe(ExitCodes.Ok);
            }

            var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            string[] written =
            [
                "scope/setup.yaml", "scope/audits/grc-platform-essential-eight-2023.yaml", "scope/audits/office-firewall-essential-eight-2023.yaml",
                $"onboarding/grc-platform-essential-eight-2023-{TutorialDate}.md", $"onboarding/office-firewall-essential-eight-2023-{TutorialDate}.md",
            ];
            foreach (var file in written)
            {
                var committed = (await File.ReadAllTextAsync(Path.Combine(expected, file), ct)).Replace(TutorialDate, today, StringComparison.Ordinal);
                var actual = await File.ReadAllTextAsync(Path.Combine(repo, file.Replace(TutorialDate, today, StringComparison.Ordinal)), ct);
                WithoutSchemaComment(actual).ShouldBe(WithoutSchemaComment(committed), file);
            }
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    private static void PathsAndCommandsExist(string text)
    {
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
    [InlineData("hound scope plan scope/ extra")]
    [InlineData("hound bogus")]
    public void A_command_or_option_that_does_not_exist_is_caught(string line)
    {
        Unknown(Command(line)!).ShouldNotBeNull();
    }

    /// <summary>A path into the template repo or Evidence Hound: relative, with a folder and no placeholders.</summary>
    private static bool IsRepoPath(string word) =>
        RepoPath().IsMatch(word) && !word.Contains("://", StringComparison.Ordinal);

    private static bool Exists(string path)
    {
        var relative = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        // A clone of this repository, as the tutorials make it with git clone.
        relative = relative.StartsWith("evidence-hound/", StringComparison.Ordinal) ? relative["evidence-hound/".Length..] : relative;
        return new[] { ExampleRepoTests.Repo, ExampleRepoTests.Tutorial, ExampleRepoTests.RepoRoot, Path.Combine(ExampleRepoTests.RepoRoot, "deploy", "compose") }
            .Any(root => Path.Exists(Path.Combine(root, relative.TrimEnd('/'))));
    }

    /// <summary>The words of a <c>hound</c> command line after <c>hound</c> (or the runner service, whose entrypoint is <c>hound</c>).</summary>
    private static string[]? Command(string line)
    {
        var match = HoundCommand().Match(line);
        return match.Success ? match.Groups["args"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
    }

    /// <summary>
    /// Why a command does not exist, or null when its subcommand and every option it uses do. The subcommand is the longest run of leading
    /// words that <c>hound</c> knows; the words after it, up to the first option, must fit the arguments its usage line names.
    /// </summary>
    private static string? Unknown(string[] args)
    {
        var words = args.TakeWhile(a => !a.StartsWith('-')).ToArray();
        for (var length = words.Length; length > 0; length--)
        {
            var path = words[..length];
            if (!SubcommandWord().IsMatch(path[^1]))
            {
                continue;
            }

            var help = Help(path);
            var usage = Regex.Match(help, $@"^  \S+ {Regex.Escape(string.Join(' ', path))}(?<rest>(?: .*)?)$", RegexOptions.Multiline, TimeSpan.FromSeconds(1));
            if (!usage.Success)
            {
                continue;
            }

            var arguments = Regex.Count(usage.Groups["rest"].Value, "<[^>]+>");
            if (words.Length - length > arguments)
            {
                return $"'hound {string.Join(' ', words)}' is not a command";
            }

            var options = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).Select(a => a.Split('=')[0]).Where(o => !help.Contains(o, StringComparison.Ordinal)).ToArray();
            return options.Length == 0 ? null : $"'hound {string.Join(' ', path)}' has no option {string.Join(", ", options)}";
        }

        return $"'hound {string.Join(' ', words)}' is not a command";
    }

    private static string Help(string[] path)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        HoundCli.RunAsync([.. path, "--help"], output, error, configureLogging: null, new Dictionary<string, string?>(), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return output.ToString();
    }

    private static async Task<int> Onboard(string[] args, string answers, CancellationToken ct)
    {
        using var input = new StringReader(await File.ReadAllTextAsync(answers, ct));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(args, input, output, error, configureLogging: null, ct);
        exit.ShouldBe(ExitCodes.Ok, error.ToString());
        return exit;
    }

    private static string WithoutSchemaComment(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("# yaml-language-server:", StringComparison.Ordinal)));

    /// <summary>Whether an address, with or without a prefix length, is in 192.0.2.0/24, 198.51.100.0/24 or 203.0.113.0/24 (RFC 5737).</summary>
    private static bool Documentation(string address) =>
        address.Split('/')[0] is var ip && (ip.StartsWith("192.0.2.", StringComparison.Ordinal) || ip.StartsWith("198.51.100.", StringComparison.Ordinal) || ip.StartsWith("203.0.113.", StringComparison.Ordinal));

    [GeneratedRegex(@"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?:/\d{1,2})?(?![\d.]*\d)")]
    private static partial Regex IPv4();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    private static IEnumerable<string> CodeBlocks(string markdown) => CodeBlock().Matches(markdown).Select(m => m.Groups["body"].Value);

    private static IEnumerable<string> Words(string block) => block.Split([' ', '\n', '\t', '"', '\'', '(', ')', '='], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"(?<!`)`(?<code>[^`\n]+)`(?!`)")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^ *```[a-z]*\n(?<body>.*?)^ *```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"(?:^|\s)(?:hound|docker compose run --rm runner)\s+(?<args>[^|>&;#\n]*)")]
    private static partial Regex HoundCommand();

    [GeneratedRegex(@"^[a-z][a-z-]*$")]
    private static partial Regex SubcommandWord();

    /// <summary>Relative paths such as <c>scope/access.yaml</c>, <c>deploy/</c> or <c>./manage.sh</c>; not <c>/run/secrets/…</c>, <c>~/…</c> or <c>&lt;name&gt;</c>.</summary>
    [GeneratedRegex(@"^(?:\./)?(?:[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]*$|^\./[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPath();
}
