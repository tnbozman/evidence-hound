using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-38: <c>hound agent setup</c> writes the assistant files for Claude Code and GitHub Copilot into the compliance repo from one source
/// of skills. Every file opens with the same rules, carries hound's marker, and a re-run replaces only the files hound wrote.
/// </summary>
public sealed partial class AgentSetupTests : IDisposable
{
    internal static readonly string[] Skills =
    [
        "onboard-a-system-with-hound", "assess-a-supplier", "read-an-api-specification", "import-an-audit-workbook", "write-a-profile", "review-before-apply",
        "onboard-the-organisation", "onboard-the-environment", "onboard-a-third-party", "onboard-an-audit", "process-manual-evidence", "weekly-review",
        "move-registers-from-spreadsheets", "prepare-the-reports", "answer-a-prime-questionnaire",
        "work-through-pending-decisions", "operate-collectors", "build-the-ssp", "respond-to-an-incident", "keep-frameworks-and-libraries", "keep-the-sprs-score", "hound-commands",
    ];

    internal static readonly string[] Rules =
    [
        "Organisation data stays in this private repo.",
        "Never open `secrets/`",
        "Never approve on the person's behalf.",
        "Nothing reaches CISO Assistant except through `hound scope plan` and `hound scope apply`.",
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("hound-agent-setup-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Claude_files_are_written_from_the_skill_source()
    {
        var (exit, output, error) = await SetupAsync("", "--assistant", "claude");

        exit.ShouldBe(ExitCodes.Ok, error);
        File.Exists(Path.Combine(_root, "CLAUDE.md")).ShouldBeTrue();
        foreach (var skill in Skills)
        {
            var text = await ReadAsync(".claude", "skills", skill, "SKILL.md");
            text.ShouldStartWith($"---\nname: {skill}\ndescription: ");
            output.ShouldContain($"Wrote .claude/skills/{skill}/SKILL.md");
        }

        // One source: a skill carries hound's default guidance for its step, word for word.
        var defaults = Path.Combine(ExampleRepoTests.RepoRoot, "skills");
        (await ReadAsync(".claude", "skills", "assess-a-supplier", "SKILL.md")).ShouldContain(Body(await File.ReadAllTextAsync(Path.Combine(defaults, "supplier-assessment.md"), _ct)));
        (await ReadAsync(".claude", "skills", "review-before-apply", "SKILL.md")).ShouldContain(Body(await File.ReadAllTextAsync(Path.Combine(defaults, "reviewing-a-config-change.md"), _ct)));
        (await ReadAsync(".claude", "skills", "onboard-a-system-with-hound", "SKILL.md")).ShouldContain("hound onboard <system> pending --json");
        (await ReadAsync(".claude", "skills", "import-an-audit-workbook", "SKILL.md")).ShouldContain("hound scope validate");
        // s13-08: the adoption and reporting guides reach the agent as skills, each linking its guide.
        (await ReadAsync(".claude", "skills", "move-registers-from-spreadsheets", "SKILL.md")).ShouldContain("docs/adopt/migrate-from-spreadsheets.md");
        (await ReadAsync(".claude", "skills", "prepare-the-reports", "SKILL.md")).ShouldContain("hound report committee");
        (await ReadAsync(".claude", "skills", "prepare-the-reports", "SKILL.md")).ShouldContain("docs/govern/ai-assisted-reporting.md");
        (await ReadAsync(".claude", "skills", "answer-a-prime-questionnaire", "SKILL.md")).ShouldContain("hound mapping crosswalk");

        var onboarding = await ReadAsync(".claude", "agents", "hound-onboarding.md");
        onboarding.ShouldStartWith("---\nname: hound-onboarding\ndescription: ");
        onboarding.ShouldContain("\ntools: Read, Grep, Glob, Edit, Write, Bash\n");
        var review = await ReadAsync(".claude", "agents", "hound-review.md");
        review.ShouldContain("\ntools: Read, Grep, Glob, Bash\n");

        Directory.Exists(Path.Combine(_root, ".github")).ShouldBeFalse();
    }

    [Fact]
    public async Task Copilot_agents_have_frontmatter_and_least_tools()
    {
        var (exit, _, error) = await SetupAsync("", "--assistant", "copilot");

        exit.ShouldBe(ExitCodes.Ok, error);
        var onboarding = await ReadAsync(".github", "agents", "hound-onboarding.agent.md");
        onboarding.ShouldStartWith("---\nname: hound-onboarding\ndescription: ");
        onboarding.ShouldContain("\ntools: [\"read\", \"search\", \"edit\", \"execute\"]\n");
        onboarding.ShouldContain("\nskills: [\"onboard-the-organisation\", \"onboard-the-environment\", \"onboard-a-third-party\", \"assess-a-supplier\", \"onboard-a-system-with-hound\", \"read-an-api-specification\", \"onboard-an-audit\", \"import-an-audit-workbook\", \"write-a-profile\", \"process-manual-evidence\", \"weekly-review\", \"move-registers-from-spreadsheets\", \"prepare-the-reports\", \"answer-a-prime-questionnaire\", \"hound-commands\"]\n");

        // The review agent reads and runs hound; it cannot edit a file.
        var review = await ReadAsync(".github", "agents", "hound-review.agent.md");
        review.ShouldContain("\ntools: [\"read\", \"search\", \"execute\"]\n");
        review.ShouldContain("\nskills: [\"review-before-apply\", \"hound-commands\"]\n");

        File.Exists(Path.Combine(_root, ".github", "copilot-instructions.md")).ShouldBeTrue();
        foreach (var skill in Skills)
        {
            (await ReadAsync(".github", "skills", skill, "SKILL.md")).ShouldStartWith($"---\nname: {skill}\ndescription: ");
        }

        Directory.Exists(Path.Combine(_root, ".claude")).ShouldBeFalse();
        File.Exists(Path.Combine(_root, "CLAUDE.md")).ShouldBeFalse();

        // Asked when not given; both writes one copy of the skills, which Copilot reads from .claude/skills too.
        (exit, _, error) = await SetupAsync("both\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Which assistant do you use in VS Code (claude, copilot or both)");
        File.Exists(Path.Combine(_root, ".claude", "skills", "assess-a-supplier", "SKILL.md")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, ".github", "skills", "assess-a-supplier", "SKILL.md")).ShouldBeFalse();
        File.Exists(Path.Combine(_root, ".github", "agents", "hound-review.agent.md")).ShouldBeTrue();

        (exit, _, error) = await SetupAsync("", "--assistant", "other");
        exit.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("claude, copilot or both");
    }

    [Fact]
    public async Task Rerun_updates_hounds_files_and_keeps_the_persons()
    {
        // The person's own instructions, and a skill of their own beside hound's.
        await WriteAsync("# Our own instructions\n", "CLAUDE.md");
        await WriteAsync("---\nname: our-own\ndescription: Ours.\n---\nOur own skill.\n", ".claude", "skills", "our-own", "SKILL.md");
        (await SetupAsync("", "--assistant", "claude")).Exit.ShouldBe(ExitCodes.Ok);

        (await ReadAsync("CLAUDE.md")).ShouldBe("# Our own instructions\n");
        var skill = await ReadAsync(".claude", "skills", "assess-a-supplier", "SKILL.md");
        Marker().IsMatch(skill).ShouldBeTrue(skill);

        // A file hound wrote that drifted is put back; one the person took over (no marker) and their own files stay.
        await WriteAsync(skill + "\nA line added by hand.\n", ".claude", "skills", "assess-a-supplier", "SKILL.md");
        await WriteAsync("---\nname: hound-review\ndescription: Ours now.\n---\nOur own review agent.\n", ".claude", "agents", "hound-review.md");
        var (exit, output, error) = await SetupAsync("", "--assistant", "claude");

        exit.ShouldBe(ExitCodes.Ok, error);
        (await ReadAsync(".claude", "skills", "assess-a-supplier", "SKILL.md")).ShouldBe(skill);
        (await ReadAsync(".claude", "agents", "hound-review.md")).ShouldContain("Our own review agent.");
        (await ReadAsync(".claude", "skills", "our-own", "SKILL.md")).ShouldContain("Our own skill.");
        (await ReadAsync("CLAUDE.md")).ShouldBe("# Our own instructions\n");
        output.ShouldContain("Updated .claude/skills/assess-a-supplier/SKILL.md");
        output.ShouldContain("Kept CLAUDE.md");
        output.ShouldContain("Kept .claude/agents/hound-review.md");
        output.ShouldContain("Unchanged .claude/skills/write-a-profile/SKILL.md");
    }

    [Fact]
    public async Task Every_file_starts_with_the_rules()
    {
        (await SetupAsync("", "--assistant", "claude")).Exit.ShouldBe(ExitCodes.Ok);
        (await SetupAsync("", "--assistant", "copilot")).Exit.ShouldBe(ExitCodes.Ok);

        string[] written =
        [
            Path.Combine(_root, "CLAUDE.md"),
            .. Directory.GetFiles(Path.Combine(_root, ".claude"), "*.md", SearchOption.AllDirectories),
            .. Directory.GetFiles(Path.Combine(_root, ".github"), "*.md", SearchOption.AllDirectories),
        ];
        written.Length.ShouldBe(1 + Skills.Length + 4 + 4 + 1 + Skills.Length);
        foreach (var file in written)
        {
            // After any frontmatter comes hound's marker with its version, then the rules, before anything else.
            var body = Body(await File.ReadAllTextAsync(file, _ct));
            var marker = Marker().Match(body);
            (marker.Success && marker.Index == 0).ShouldBeTrue(file);
            var rest = body[marker.Length..].TrimStart();
            rest.ShouldStartWith("## Rules", Case.Sensitive, file);
            var rules = rest[..rest.IndexOf("\n## ", 1, StringComparison.Ordinal)];
            foreach (var rule in Rules)
            {
                rules.ShouldContain(rule, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public async Task The_profile_skill_says_where_the_library_and_the_guide_are_since_neither_is_in_the_compliance_repo()
    {
        (await SetupAsync("", "--assistant", "claude")).Exit.ShouldBe(ExitCodes.Ok);

        var skill = await ReadAsync(".claude", "skills", "write-a-profile", "SKILL.md");

        skill.ShouldContain("https://github.com/tnbozman/evidence-hound/tree/main/profiles");
        skill.ShouldContain("https://github.com/tnbozman/evidence-hound/blob/main/docs/collectors/rest.md");
        skill.ShouldContain("profiles_path");
        skill.ShouldNotContain("Follow docs/collectors/rest.md");
    }

    [Fact]
    public async Task The_onboarding_skill_names_the_environment_stage_the_profile_library_and_how_a_source_is_written()
    {
        (await SetupAsync("", "--assistant", "claude")).Exit.ShouldBe(ExitCodes.Ok);

        var skill = await ReadAsync(".claude", "skills", "onboard-a-system-with-hound", "SKILL.md");

        // Found in a rehearsal: an agent decided 107 firewall requirements one by one, guessed how to write a source from a
        // library profile, and named the system's audit from an example with a SYS- prefix the system did not have.
        skill.ShouldContain("onboard-the-environment");
        skill.ShouldContain("https://github.com/tnbozman/evidence-hound/tree/main/profiles");
        skill.ShouldContain("${data_region}");
        skill.ShouldContain("EX-E8-OFFICE-FIREWALL");
    }

    [Fact]
    public async Task Init_offers_the_setup_at_the_end()
    {
        var repo = Path.Combine(_root, "compliance");

        // Seven answers for the repo itself, then the assistant.
        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["init", repo], "\n\n\n\n\n\n\ncopilot\n", terminal: false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Set up an AI assistant in this repo (claude, copilot, both or none)");
        File.Exists(Path.Combine(repo, ".github", "agents", "hound-onboarding.agent.md")).ShouldBeTrue();
        output.ShouldContain("Wrote .github/copilot-instructions.md");

        // With --yes nothing is asked and nothing is set up; the next steps name the command.
        var quiet = Path.Combine(_root, "quiet");
        (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["init", quiet, "--yes"], "", terminal: false, Path.Combine(_root, "home"), _ct);
        exit.ShouldBe(ExitCodes.Ok, error);
        Directory.Exists(Path.Combine(quiet, ".github")).ShouldBeFalse();
        output.ShouldContain("hound agent setup");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A Markdown file without its frontmatter.</summary>
    private static string Body(string text)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalised.StartsWith("---\n", StringComparison.Ordinal) ? normalised[(normalised.IndexOf("\n---\n", StringComparison.Ordinal) + 5)..].Trim() : normalised.Trim();
    }

    private Task<string> ReadAsync(params string[] path) => File.ReadAllTextAsync(Path.Combine([_root, .. path]), _ct);

    private async Task WriteAsync(string text, params string[] path)
    {
        var file = Path.Combine([_root, .. path]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text, _ct);
    }

    private Task<(int Exit, string Output, string Error)> SetupAsync(string answers, params string[] options) =>
        OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", _root, .. options], answers, terminal: false, Path.Combine(_root, "home"), _ct);

    [GeneratedRegex(@"<!-- Written by hound agent setup \(hound [0-9][^)\s]*\)\. [^\n]* -->", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
