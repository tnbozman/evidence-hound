using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s18-03: the agent kit and the guides name only commands and options the CLI has, and every skill an agent, <c>hound next</c> or the
/// review queue names exists. Read from the sources in <c>agent/</c> and <c>docs/</c>; the sprint files, designs and recorded evidence are history and are
/// left out.
/// </summary>
public sealed partial class AgentFilesTests
{
    private static readonly string Root = ExampleRepoTests.RepoRoot;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_hound_command_in_a_skill_exists()
    {
        var options = await OptionsAsync();

        var wrong = Sources()
            .SelectMany(f => Lines(File.ReadAllText(f)).Select(line => (File: Path.GetRelativePath(Root, f), Line: line)))
            .Select(n => (n.File, n.Line, Problem: Check(n.Line, options)))
            .Where(n => n.Problem is not null)
            .Select(n => $"{n.File}: hound {n.Line}: {n.Problem}")
            .Distinct()
            .ToArray();

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_hound_command_in_a_tutorial_exists()
    {
        // s20-01: the interactive tutorials teach only commands and options the CLI has, so a renamed command fails here, not for a learner.
        var options = await OptionsAsync();
        var commands = Directory.GetFiles(Path.Combine(Root, "docs", "tutorials", "interactive"), "*.json")
            .SelectMany(f => TutorialCommands(File.ReadAllText(f)).Select(line => (File: Path.GetRelativePath(Root, f), Line: line)))
            .ToArray();

        var wrong = commands
            .Select(n => (n.File, n.Line, Problem: Check(n.Line, options)))
            .Where(n => n.Problem is not null)
            .Select(n => $"{n.File}: hound {n.Line}: {n.Problem}")
            .Distinct()
            .ToArray();

        commands.Length.ShouldBeGreaterThan(20);
        wrong.ShouldBeEmpty();
    }

    [Fact]
    public void Every_skill_an_agent_names_exists()
    {
        var skills = Directory.GetFiles(Path.Combine(Root, "agent", "skills"), "*.md").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var agents = Directory.GetFiles(Path.Combine(Root, "agent", "agents"), "*.md")
            .SelectMany(f => AgentSkills().Match(File.ReadAllText(f)) is { Success: true } m ? m.Groups["list"].Value.Split(',', StringSplitOptions.TrimEntries) : [])
            .ToArray();
        var named = Directory.GetFiles(Path.Combine(Root, "agent"), "*.md", SearchOption.AllDirectories)
            .SelectMany(f => NamedSkill().Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .ToArray();
        var code = Directory.GetFiles(Path.Combine(Root, "src", "EvidenceHound.Core", "Review"), "*.cs")
            .SelectMany(f => CodeSkill().Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .ToArray();

        agents.ShouldNotBeEmpty();
        code.ShouldContain("work-through-pending-decisions");
        agents.Concat(named).Concat(code).Distinct().Where(s => !skills.Contains(s)).ShouldBeEmpty();
        skills.Where(s => !agents.Contains(s)).ShouldBeEmpty("each skill belongs to an agent");
    }

    /// <summary>The guides and agent files people and agents follow.</summary>
    private static IEnumerable<string> Sources() =>
        Directory.GetFiles(Path.Combine(Root, "agent"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "docs"), "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}sprints{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}design{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}evidence{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            .Append(Path.Combine(Root, "README.md"));

    /// <summary>What follows <c>hound </c> in each inline code span and each line of a code block.</summary>
    private static IEnumerable<string> Lines(string text) =>
        Inline().Matches(text).Select(m => m.Groups["rest"].Value)
            .Concat(Block().Matches(text).SelectMany(b => b.Groups["body"].Value.Split('\n'))
                .Select(l => BlockLine().Match(l)).Where(m => m.Success).Select(m => m.Groups["rest"].Value));

    /// <summary>What follows <c>hound </c> in each terminal command and "try it" answer of a tutorial.</summary>
    private static IEnumerable<string> TutorialCommands(string json)
    {
        using var tutorial = JsonDocument.Parse(json);
        foreach (var step in tutorial.RootElement.GetProperty("steps").EnumerateArray())
        {
            var lines = step.TryGetProperty("terminal", out var terminal) ? terminal.EnumerateArray().Select(t => t.GetProperty("command").GetString()) : [];
            var answer = step.TryGetProperty("try", out var tryIt) ? tryIt.GetProperty("answer").GetString() : null;
            foreach (var command in lines.Append(answer))
            {
                if (command is not null && command.StartsWith("hound ", StringComparison.Ordinal))
                {
                    yield return command["hound ".Length..];
                }
            }
        }
    }

    /// <summary>Null when the command exists and has each option the line gives; otherwise what is wrong.</summary>
    private static string? Check(string line, Dictionary<string, HashSet<string>> options)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // The words before the first option, less arguments such as <system> or EXAMPLE-ASSETS: "onboard <system> pending" is "onboard pending".
        var command = words.TakeWhile(w => !w.StartsWith('-')).Where(w => CommandWord().IsMatch(w)).ToArray();
        string? found = null;
        for (var length = command.Length; length > 0 && found is null; length--)
        {
            var prefix = string.Join(' ', command[..length]);
            found = options.ContainsKey(prefix) ? prefix : null;
        }

        if (found is null)
        {
            return command.Length == 0 ? null : "no such command";
        }

        var unknown = words.Select(w => Option().Match(w)).Where(m => m.Success).Select(m => m.Value).Where(o => !options[found].Contains(o)).ToArray();
        return unknown.Length == 0 ? null : $"'{found}' has no {string.Join(", ", unknown)}";
    }

    /// <summary>Every command path with the options its <c>--help</c> lists.</summary>
    private async Task<Dictionary<string, HashSet<string>>> OptionsAsync()
    {
        var all = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var path in await CheatsheetTests.CommandsAsync([], _ct))
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            await HoundCli.RunAsync([.. path.Split(' '), "--help"], output, error, null, new Dictionary<string, string?>(), _ct);
            all[path] = output.ToString().Split('\n').SkipWhile(l => l.Trim() != "Options:").Skip(1)
                .TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal))
                .SelectMany(l => Option().Matches(l.Split("  ", StringSplitOptions.RemoveEmptyEntries)[0]).Select(m => m.Value))
                .ToHashSet(StringComparer.Ordinal);
        }

        return all;
    }

    [GeneratedRegex(@"`hound (?<rest>[^`\n]*)`")]
    private static partial Regex Inline();

    [GeneratedRegex(@"^```[a-z]*\n(?<body>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex(@"^\s*(?:\$ )?hound (?<rest>.*)$")]
    private static partial Regex BlockLine();

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex CommandWord();

    [GeneratedRegex("--[a-z][a-z0-9-]*")]
    private static partial Regex Option();

    [GeneratedRegex(@"^skills: \[(?<list>[^\]]*)\]", RegexOptions.Multiline)]
    private static partial Regex AgentSkills();

    [GeneratedRegex(@"`(?<name>[a-z]+(?:-[a-z]+)+)` skill")]
    private static partial Regex NamedSkill();

    [GeneratedRegex(@"(?:Skill = |=> )""(?<name>[a-z]+(?:-[a-z]+){2,})""")]
    private static partial Regex CodeSkill();
}
