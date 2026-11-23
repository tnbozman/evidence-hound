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

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
