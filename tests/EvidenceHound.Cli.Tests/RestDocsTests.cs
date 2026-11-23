using System.Text.RegularExpressions;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-14: the rest collector's reference keeps up with the engine, its links work, and the write-a-profile tutorial, followed as
/// written, gives a profile that passes its tests.
/// </summary>
public sealed partial class RestDocsTests : IDisposable
{
    private static readonly string Docs = Path.Combine(ExampleRepoTests.RepoRoot, "docs");

    private static readonly string Reference = File.ReadAllText(Path.Combine(Docs, "collectors", "rest.md"));

    private static readonly string Tutorial = File.ReadAllText(Path.Combine(Docs, "tutorials", "write-a-profile.md"));

    private readonly string _work = Directory.CreateTempSubdirectory("hound-write-a-profile-").FullName;

    [Fact]
    public void Every_operator_and_result_is_documented()
    {
        string[] names =
        [
            .. ProfileParser.Operators.Keys,
            .. ProfileParser.EmptyResults.Keys,
            .. ProfileParser.MissingRules.Keys,
            .. ProfileParser.ParameterTypes.Keys,
            .. ProfileParser.AuthTypes,
            .. ProfileParser.PagingStyles,
            "all", "any", "none", "count_at_most", "count_at_least", "ratio_at_least", "partial_at", "negate",
            "pass", "partial", "unable to assess", "not applicable",
        ];

        names.Distinct().Where(name => !Reference.Contains($"`{name}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_relative_link_resolves()
    {
        var broken = new[] { ("collectors", Reference), ("tutorials", Tutorial) }
            .SelectMany(doc => Link().Matches(doc.Item2).Select(m => (Folder: doc.Item1, Target: m.Groups["target"].Value)))
            .Where(link => !link.Target.Contains("://", StringComparison.Ordinal) && !link.Target.StartsWith('#'))
            .Select(link => Path.GetFullPath(Path.Combine(Docs, link.Folder, link.Target.Split('#')[0])))
            .Where(path => !File.Exists(path) && !Directory.Exists(path))
            .ToArray();

        broken.ShouldBeEmpty();
    }

    [Fact]
    public void Design_is_marked_implemented()
    {
        File.ReadAllText(Path.Combine(Docs, "design", "rest-collector.md")).ShouldContain("Status: Implemented");
    }

    [Fact]
    public async Task Tutorial_followed_as_written_gives_a_passing_profile()
    {
        var files = FileBlock().Matches(Tutorial).GroupBy(m => m.Groups["path"].Value).ToDictionary(g => g.Key, g => g.Last().Groups["body"].Value);
        files.Keys.ShouldContain("my-profiles/example-devices.yaml");
        files.Keys.ShouldContain("my-profiles/example-devices.tests/all-supported.yaml");
        foreach (var (path, body) in files)
        {
            var target = Path.Combine(_work, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, body, TestContext.Current.CancellationToken);
        }

        Tutorial.ShouldContain("cp -R profiles/example.tests/recordings my-profiles/example-devices.tests/");
        CopyFolder(Path.Combine(ExampleRepoTests.RepoRoot, "profiles", "example.tests", "recordings"), Path.Combine(_work, "my-profiles", "example-devices.tests", "recordings"));

        var result = await ProfileTestRunner.RunAsync(Path.Combine(_work, "my-profiles"), "example-devices", TestContext.Current.CancellationToken);

        var cases = result.ShouldBeOfType<Result<IReadOnlyList<CaseReport>>.Ok>().Value;
        cases.Count.ShouldBe(files.Keys.Count(k => k.Contains(".tests/", StringComparison.Ordinal)));
        cases.Where(c => !c.Passed).Select(c => c.Name).ShouldBeEmpty();
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }
    }

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();

    /// <summary>A YAML block whose first line is <c># file: path</c>: the tutorial's version of that file at that point.</summary>
    [GeneratedRegex(@"```yaml\n# file: (?<path>\S+)\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex FileBlock();
}
