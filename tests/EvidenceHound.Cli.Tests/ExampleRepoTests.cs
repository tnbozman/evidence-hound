using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s06-12: the example compliance repo is complete, valid, schema-linked, and in step with the repository's own mappings and snapshots.</summary>
public sealed partial class ExampleRepoTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string Repo => Path.Combine(RepoRoot, "examples", "compliance-repo");

    [Fact]
    public async Task Example_compliance_repo_validates()
    {
        (await Hound("config", "validate", Path.Combine(Repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString());
        (await Hound("scope", "validate", Path.Combine(Repo, "scope"), "--config", Path.Combine(Repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString());
        foreach (var mapping in Directory.GetFiles(Path.Combine(Repo, "mappings"), "*.yaml"))
        {
            (await Hound("mapping", "validate", mapping)).ShouldBe(ExitCodes.Ok, $"{mapping}: {_error}");
        }
    }

    [Fact]
    public void Example_compliance_repo_has_the_config_scope_mappings_snapshots_manual_sources_assistant_guide_and_workflow()
    {
        string[] expected =
        [
            "README.md", "CLAUDE.md", "hound.yaml", ".github/workflows/compliance.yml", "manual-sources/README.md",
            "scope/setup.yaml", "scope/controls.yaml", "mappings/essential-eight-2023.yaml", "frameworks/essential-eight-2023.json",
        ];

        expected.Where(f => !File.Exists(Path.Combine(Repo, f))).ShouldBeEmpty();
    }

    [Fact]
    public void Every_yaml_file_but_the_workflow_links_a_schema_that_exists()
    {
        var files = Directory.GetFiles(Repo, "*.yaml", SearchOption.AllDirectories).Where(f => !f.Contains(".github", StringComparison.Ordinal)).ToArray();

        files.ShouldNotBeEmpty();
        foreach (var file in files)
        {
            var first = File.ReadLines(file).First();
            var match = SchemaComment().Match(first);
            match.Success.ShouldBeTrue($"{file} starts with '{first}'");
            File.Exists(Path.GetFullPath(match.Groups["schema"].Value, Path.GetDirectoryName(file)!)).ShouldBeTrue($"{file} links {match.Groups["schema"].Value}");
        }
    }

    [Fact]
    public void Mappings_and_snapshots_are_the_same_as_the_repository_ones()
    {
        foreach (var (copy, original) in new[]
        {
            ("mappings/essential-eight-2023.yaml", "mappings/essential-eight-2023.yaml"),
            ("mappings/example.yaml", "mappings/example.yaml"),
            ("frameworks/essential-eight-2023.json", "frameworks/essential-eight-2023.json"),
            ("frameworks/asd-essentials-enterprise-it.json", "frameworks/asd-essentials-enterprise-it.json"),
        })
        {
            WithoutSchemaComment(Path.Combine(Repo, copy)).ShouldBe(WithoutSchemaComment(Path.Combine(RepoRoot, original)), copy);
        }
    }

    [Fact]
    public void Relative_links_in_the_docs_and_the_example_repo_point_at_files_that_exist()
    {
        var documents = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Repo, "*.md", SearchOption.AllDirectories))
            .Append(Path.Combine(RepoRoot, "README.md"));
        var broken = documents
            .SelectMany(document => MarkdownLink().Matches(File.ReadAllText(document))
                .Select(m => m.Groups["target"].Value)
                .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith('#') && !target.StartsWith("mailto:", StringComparison.Ordinal))
                .Select(target => (Document: document, Target: target.Split('#')[0]))
                .Where(link => link.Target.Length > 0 && !Path.Exists(Path.GetFullPath(link.Target, Path.GetDirectoryName(link.Document)!))))
            .Select(link => $"{Path.GetRelativePath(RepoRoot, link.Document)} -> {link.Target}")
            .ToArray();

        broken.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("No global.json above the test folder.");
        }
    }

    private static string[] WithoutSchemaComment(string path) =>
        [.. File.ReadAllLines(path).Where(l => !l.StartsWith("# yaml-language-server:", StringComparison.Ordinal))];

    private Task<int> Hound(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct);

    [GeneratedRegex(@"^# yaml-language-server: \$schema=(?<schema>\S+)$")]
    private static partial Regex SchemaComment();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
