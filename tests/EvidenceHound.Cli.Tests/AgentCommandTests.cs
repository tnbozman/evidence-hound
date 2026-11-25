using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s15-13: the skills and agents hound writes for Claude Code and GitHub Copilot know every hound command and name only real ones. The
/// command reference is built from the CLI itself when <c>hound agent setup</c> runs, so it cannot fall behind; every top-level command has
/// a skill; and every <c>hound ...</c> command a skill, agent or the instructions name exists. Each check runs on what is written for both.
/// </summary>
public sealed partial class AgentCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-agent-commands-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<string> Assistants => ["claude", "copilot"];

    [Theory]
    [MemberData(nameof(Assistants))]
    public async Task The_reference_names_every_command_for_both_assistants(string assistant)
    {
        var files = await SetupAsync(assistant);
        var reference = files[Reference(assistant)];

        var commands = await CheatsheetTests.CommandsAsync([], _ct);

        commands.Count.ShouldBeGreaterThan(40);
        commands.Where(c => !reference.Contains($"`hound {c}", StringComparison.Ordinal)).ShouldBeEmpty();
        reference.ShouldContain("--json");
    }

    [Theory]
    [MemberData(nameof(Assistants))]
    public async Task Every_top_level_command_has_a_skill(string assistant)
    {
        var files = await SetupAsync(assistant);
        var guidance = string.Concat(files.Where(f => f.Key != Reference(assistant)).Select(f => f.Value));

        var topLevel = (await CheatsheetTests.CommandsAsync([], _ct)).Where(c => !c.Contains(' ', StringComparison.Ordinal)).ToArray();

        topLevel.Where(c => !Regex.IsMatch(guidance, $@"`hound {Regex.Escape(c)}[ `]")).ShouldBeEmpty("each top-level command is used by a skill, an agent or the instructions");
    }

    [Theory]
    [MemberData(nameof(Assistants))]
    public async Task Every_command_a_skill_names_exists(string assistant)
    {
        var files = await SetupAsync(assistant);
        var (paths, takesArguments) = Commands(files[Reference(assistant)]);

        var unknown = files.Where(f => f.Key != Reference(assistant))
            .SelectMany(f => Named().Matches(f.Value).Select(m => (File: f.Key, Words: Words(m.Groups["rest"].Value))))
            .Where(n => !Known(n.Words, paths, takesArguments))
            .Select(n => $"{n.File}: hound {string.Join(' ', n.Words)}")
            .Distinct()
            .ToArray();

        unknown.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Assistants))]
    public async Task Every_agent_loads_the_command_reference(string assistant)
    {
        var files = await SetupAsync(assistant);

        var agents = files.Where(f => f.Key.Contains("/agents/", StringComparison.Ordinal)).ToArray();

        agents.Length.ShouldBeGreaterThanOrEqualTo(4);
        agents.Where(a => !a.Value.Contains("hound-commands", StringComparison.Ordinal)).Select(a => a.Key).ShouldBeEmpty();
        files[assistant == "claude" ? "CLAUDE.md" : ".github/copilot-instructions.md"].ShouldContain("hound-commands");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Reference(string assistant) => $".{(assistant == "claude" ? "claude" : "github")}/skills/hound-commands/SKILL.md";

    /// <summary>Every file hound agent setup writes for <paramref name="assistant"/>, by its path in the repo.</summary>
    private async Task<Dictionary<string, string>> SetupAsync(string assistant)
    {
        var repo = Path.Combine(_root, assistant);
        var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(["agent", "setup", "--repo", repo, "--assistant", assistant], "", terminal: false, Path.Combine(_root, "home"), _ct);
        exit.ShouldBe(ExitCodes.Ok, error);
        return Directory.GetFiles(repo, "*.md", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(repo, f).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
    }

    /// <summary>The command paths the reference lists, and those that take arguments (a heading such as <c>`hound init [&lt;folder&gt;]`</c>).</summary>
    private static (HashSet<string> Paths, HashSet<string> TakesArguments) Commands(string reference)
    {
        var headings = Heading().Matches(reference).Select(m => m.Groups["command"].Value).ToArray();
        var paths = headings.Select(h => string.Join(' ', Words(h))).ToHashSet(StringComparer.Ordinal);
        var takes = headings.Where(h => h.Contains('<', StringComparison.Ordinal)).Select(h => string.Join(' ', Words(h))).ToHashSet(StringComparer.Ordinal);
        return (paths, takes);
    }

    /// <summary>The command words: up to the first option, argument, path or value.</summary>
    private static string[] Words(string text) => [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(w => CommandWord().IsMatch(w))];

    /// <summary>
    /// Known when the longest prefix of <paramref name="words"/> that is a command is all of them, or is a command that takes arguments
    /// (the rest are its values), or has no sub-commands.
    /// </summary>
    private static bool Known(string[] words, HashSet<string> paths, HashSet<string> takesArguments)
    {
        for (var length = words.Length; length > 0; length--)
        {
            var prefix = string.Join(' ', words[..length]);
            if (paths.Contains(prefix))
            {
                return length == words.Length || takesArguments.Contains(prefix) || !paths.Any(p => p.StartsWith(prefix + " ", StringComparison.Ordinal));
            }
        }

        return false;
    }

    [GeneratedRegex(@"`hound (?<rest>[^`\n]*)`")]
    private static partial Regex Named();

    [GeneratedRegex(@"^#+ `hound (?<command>[^`]*)`", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex CommandWord();
}
