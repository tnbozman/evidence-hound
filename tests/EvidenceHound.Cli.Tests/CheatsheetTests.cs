using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>The one-page command cheat sheet names every command and sub-command the CLI has, so it cannot fall behind.</summary>
public sealed class CheatsheetTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_command_and_sub_command_is_on_the_cheat_sheet()
    {
        var sheet = await File.ReadAllTextAsync(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "cheatsheet.md"), _ct);

        var commands = await CommandsAsync([]);

        commands.Count.ShouldBeGreaterThan(40);
        commands.Where(c => !sheet.Contains($"`hound {c}", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    /// <summary>Every command path under <paramref name="parent"/>, read from <c>--help</c>.</summary>
    private async Task<List<string>> CommandsAsync(string[] parent)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await HoundCli.RunAsync([.. parent, "--help"], output, error, logging => logging.AddProvider(new RecordingLoggerProvider()), new Dictionary<string, string?>(), _ct);
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
            found.AddRange(await CommandsAsync(path));
        }

        return found;
    }
}
