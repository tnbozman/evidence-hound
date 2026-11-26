using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s19-01: <c>docs/repo-map.md</c> names every folder in the repository with its purpose, who uses it and a verdict, so nothing new
/// appears without a reason; and no build output is committed.
/// </summary>
public sealed partial class RepoMapTests
{
    private static readonly string Root = ExampleRepoTests.RepoRoot;

    /// <summary>The folders whose child folders the map lists one by one; elsewhere a top-level folder is enough.</summary>
    private static readonly string[] Expanded = ["src", "tests", "docs", "examples", "scripts"];

    [Fact]
    public void Every_folder_is_in_the_map()
    {
        var map = Path.Combine(Root, "docs", "repo-map.md");
        File.Exists(map).ShouldBeTrue("docs/repo-map.md lists every folder");
        var listed = MapEntry().Matches(File.ReadAllText(map)).Select(m => m.Groups["path"].Value.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);

        var folders = Tracked()
            .Select(f => f.Split('/'))
            .Where(parts => parts.Length > 1)
            .SelectMany(parts => Expanded.Contains(parts[0]) && parts.Length > 2 ? new[] { parts[0], $"{parts[0]}/{parts[1]}" } : [parts[0]])
            .Distinct(StringComparer.Ordinal);

        folders.Where(f => !listed.Contains(f)).Order(StringComparer.Ordinal).ShouldBeEmpty("folders missing from docs/repo-map.md");
        listed.Where(l => !Directory.Exists(Path.Combine(Root, l))).Order(StringComparer.Ordinal).ShouldBeEmpty("map entries with no folder");
    }

    [Fact]
    public void No_build_output_is_committed()
    {
        Tracked().Where(f => BuildOutput().IsMatch(f)).ShouldBeEmpty();
    }

    private static string[] Tracked()
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", Root, "ls-files", "-z"]) { RedirectStandardOutput = true })!;
        var files = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        git.ExitCode.ShouldBe(0, "git ls-files");
        return files.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>A map row: a table row whose first cell is a folder in backticks.</summary>
    [GeneratedRegex(@"^\| `(?<path>[^`]+)/` \|", RegexOptions.Multiline)]
    private static partial Regex MapEntry();

    [GeneratedRegex(@"(^|/)(bin|obj|TestResults|__pycache__|node_modules)/|\.pyc$")]
    private static partial Regex BuildOutput();
}
