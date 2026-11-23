using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Tests;

/// <summary>Relative links in the docs point at files that exist.</summary>
public partial class DocsLinkTests
{
    [Fact]
    public void Every_relative_link_in_the_docs_points_at_a_file_that_exists()
    {
        var root = ExampleRepoTests.RepoRoot;
        var pages = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Append(Path.Combine(root, "README.md"));

        var broken = pages
            .SelectMany(page => Link().Matches(File.ReadAllText(page)).Select(m => (Page: page, Target: m.Groups["target"].Value)))
            .Where(link => !link.Target.Contains("://", StringComparison.Ordinal) && !link.Target.StartsWith("mailto:", StringComparison.Ordinal))
            .Where(link => !Path.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link.Page)!, Uri.UnescapeDataString(link.Target)))))
            .Select(link => $"{Path.GetRelativePath(root, link.Page)} -> {link.Target}");

        broken.ShouldBeEmpty();
    }

    [GeneratedRegex(@"\]\((?<target>[^)#\s<>]+)(#[^)\s]*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}
