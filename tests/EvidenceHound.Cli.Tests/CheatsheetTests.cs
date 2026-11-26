using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>The one-page command cheat sheet names every command and sub-command the CLI has, so it cannot fall behind.</summary>
public sealed partial class CheatsheetTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_command_and_sub_command_is_on_the_cheat_sheet()
    {
        var sheet = await File.ReadAllTextAsync(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "cheatsheet.md"), _ct);

        var commands = await CommandsAsync([], _ct);

        commands.Count.ShouldBeGreaterThan(40);
        commands.Where(c => !sheet.Contains($"`hound {c}", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_option_the_cheat_sheet_names_exists_on_its_command()
    {
        // s19-07: the walk-through hit `hound irp build --config`, which the sheet listed as required and the command does not take.
        var lines = await File.ReadAllLinesAsync(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "cheatsheet.md"), _ct);
        var wrong = new List<string>();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (CommandLine().Match(lines[i]) is not { Success: true } command || !lines[i + 1].StartsWith("  - ", StringComparison.Ordinal))
            {
                continue;
            }

            var help = await HelpAsync(command.Groups["path"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), _ct);
            wrong.AddRange(Option().Matches(lines[i + 1]).Select(m => m.Value).Where(o => !help.Contains(o + ' ', StringComparison.Ordinal) && !help.Contains(o + ',', StringComparison.Ordinal))
                .Select(o => $"hound {command.Groups["path"].Value} {o}"));
        }

        wrong.ShouldBeEmpty();
    }

    private static async Task<string> HelpAsync(string[] path, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await HoundCli.RunAsync([.. path, "--help"], output, error, logging => logging.AddProvider(new RecordingLoggerProvider()), new Dictionary<string, string?>(), ct);
        return output.ToString();
    }

    [GeneratedRegex(@"^- `hound (?<path>[a-z][a-z -]*?)(?: <[^`]*)?(?: --[^`]*)?`:")]
    private static partial Regex CommandLine();

    [GeneratedRegex(@"--[a-z][a-z-]*")]
    private static partial Regex Option();

    /// <summary>Every command path under <paramref name="parent"/>, read from <c>--help</c>.</summary>
    internal static async Task<List<string>> CommandsAsync(string[] parent, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await HoundCli.RunAsync([.. parent, "--help"], output, error, logging => logging.AddProvider(new RecordingLoggerProvider()), new Dictionary<string, string?>(), ct);
        var lines = output.ToString().Split('\n');
        var names = lines.SkipWhile(l => l.Trim() != "Commands:").Skip(1)
            .Where(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Length > 2 && char.IsAsciiLetterLower(l[2]))
            .Select(l => l.Trim().Split(' ')[0])
            .ToArray();
        var found = new List<string>();
        foreach (var name in names)
        {
            string[] path = [.. parent.Where(p => !p.StartsWith('<')), name];
            found.Add(string.Join(' ', path));
            found.AddRange(await CommandsAsync(path, ct));
        }

        return found;
    }
}
