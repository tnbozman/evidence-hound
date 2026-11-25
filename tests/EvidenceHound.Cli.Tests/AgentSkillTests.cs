namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-41: every kind of onboarding has a skill in the one skill source: the organisation, the environment, a third party, a system, an
/// audit and an existing audit workbook. Each tells the agent to read the person's documents itself, to ask the person what only they
/// know, and never to approve.
/// </summary>
public sealed class AgentSkillTests : IDisposable
{
    internal static readonly (string Skill, string Command)[] Kinds =
    [
        ("onboard-the-organisation", "hound init"),
        ("onboard-the-environment", "hound onboard environment decide"),
        ("onboard-a-third-party", "hound supplier draft"),
        ("onboard-a-system-with-hound", "hound onboard <system> decide"),
        ("onboard-an-audit", "hound audit draft"),
        ("import-an-audit-workbook", "hound scope import"),
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("hound-agent-skills-").FullName;

    [Fact]
    public async Task Every_onboarding_kind_has_a_skill()
    {
        var ct = TestContext.Current.CancellationToken;
        var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", _root, "--assistant", "both"], "", terminal: false, Path.Combine(_root, "home"), ct);
        exit.ShouldBe(ExitCodes.Ok, error);

        var onboarding = await File.ReadAllTextAsync(Path.Combine(_root, ".github", "agents", "hound-onboarding.agent.md"), ct);
        foreach (var (skill, command) in Kinds)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(_root, ".claude", "skills", skill, "SKILL.md"), ct);
            text.ShouldContain(command, Case.Sensitive, skill);
            text.ShouldContain("Read the person's documents yourself, PDFs included", Case.Sensitive, skill);
            text.ShouldContain("Ask the person", Case.Sensitive, skill);
            text.ShouldContain("Never approve", Case.Sensitive, skill);
            onboarding.ShouldContain($"\"{skill}\"");
        }
    }

    /// <summary>s18-03: each feature up to s17, the skill for it, and what the skill must say.</summary>
    internal static readonly (string Skill, string[] Says)[] Features =
    [
        ("keep-the-risk-register", ["docs/risk.md", "scope/risks/", "hound library validate", "hound scope validate", "hound scope plan", "Never accept a risk"]),
        ("keep-exceptions-and-acceptances", ["docs/risk.md", "scope/exceptions.yaml", "treatment: accept", "hound status", "Never write a `status`"]),
        ("keep-the-bia", ["docs/bia.md", "scope/bia/", "tested_by", "hound scope validate", "hound scope plan"]),
        ("respond-to-an-incident", ["hound incident obligations", "hound incident report-pack", "hound irp build"]),
        ("flow-down-to-suppliers", ["docs/scope.md", "flow_down: true", "contract.flow_down_missing", "hound supplier list --data"]),
        ("keep-the-metrics", ["docs/metrics.md", "scope/metrics.yaml", "source: hound", "metric.not_applied", "hound scope plan"]),
        ("keep-the-sprs-score", ["docs/adopt/cmmc-level-2.md", "hound sprs score", "hound sprs affirmation-pack"]),
    ];

    [Fact]
    public async Task Every_feature_has_a_skill()
    {
        var ct = TestContext.Current.CancellationToken;
        var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", _root, "--assistant", "both"], "", terminal: false, Path.Combine(_root, "home"), ct);
        exit.ShouldBe(ExitCodes.Ok, error);

        var agents = string.Concat(Directory.GetFiles(Path.Combine(_root, ".github", "agents")).Select(File.ReadAllText));
        var weekly = await File.ReadAllTextAsync(Path.Combine(_root, ".claude", "skills", "weekly-review", "SKILL.md"), ct);
        foreach (var (skill, says) in Features)
        {
            var file = Path.Combine(_root, ".claude", "skills", skill, "SKILL.md");
            File.Exists(file).ShouldBeTrue(skill);
            var text = await File.ReadAllTextAsync(file, ct);
            says.Where(s => !text.Contains(s, StringComparison.Ordinal)).ShouldBeEmpty(skill);
            agents.ShouldContain(skill, Case.Sensitive);
        }

        foreach (var skill in new[] { "keep-exceptions-and-acceptances", "flow-down-to-suppliers", "respond-to-an-incident" })
        {
            weekly.ShouldContain($"\"{skill}\"", Case.Sensitive, "the weekly review sends each new kind of item to its skill");
        }
    }

    [Fact]
    public async Task Manual_evidence_skill_proposes_from_the_file_and_never_attests()
    {
        // s09-42: the agent proposes only from what the file shows, quotes what supports it, says what is missing, and never attests.
        var ct = TestContext.Current.CancellationToken;
        var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", _root, "--assistant", "claude"], "", terminal: false, Path.Combine(_root, "home"), ct);
        exit.ShouldBe(ExitCodes.Ok, error);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, ".claude", "skills", "process-manual-evidence", "SKILL.md"), ct);
        string[] says = ["hound manual propose", "--supports", "only from what the file shows", "Quote", "what is missing", "Never attest", "hound manual approve"];
        foreach (var phrase in says)
        {
            text.ShouldContain(phrase, Case.Sensitive);
        }

        (await File.ReadAllTextAsync(Path.Combine(_root, "CLAUDE.md"), ct)).ShouldContain("`hound manual approve`");
    }

    [Fact]
    public async Task Decisions_skill_recommends_and_never_accepts()
    {
        // s15-12, s15-13: the agent works through the waiting decisions with the person, one at a time, highest risk first; it records its
        // case with sources and hands the person the one command to run; it never accepts, challenges or skips for them.
        var ct = TestContext.Current.CancellationToken;
        var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", _root, "--assistant", "both"], "", terminal: false, Path.Combine(_root, "home"), ct);
        exit.ShouldBe(ExitCodes.Ok, error);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, ".claude", "skills", "work-through-pending-decisions", "SKILL.md"), ct);
        string[] says = ["hound review pending", "\"risk\"", "\"summary\"", "hound review recommend", "hound review --only <key>", "one at a time", "Never run `hound review`", "Ask the person"];
        foreach (var phrase in says)
        {
            text.ShouldContain(phrase, Case.Sensitive);
        }

        foreach (var agent in new[] { Path.Combine(_root, ".claude", "agents", "hound-decisions.md"), Path.Combine(_root, ".github", "agents", "hound-decisions.agent.md") })
        {
            (await File.ReadAllTextAsync(agent, ct)).ShouldContain("work-through-pending-decisions");
        }

        (await File.ReadAllTextAsync(Path.Combine(_root, ".claude", "skills", "weekly-review", "SKILL.md"), ct)).ShouldContain("work-through-pending-decisions");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
