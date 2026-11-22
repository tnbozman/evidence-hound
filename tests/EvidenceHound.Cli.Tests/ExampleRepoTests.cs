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

    private static readonly string[] RequiredDeploySettings = ["CA_VERSION", "CA_HOSTNAME", "TZ", "HOUND_RUNNER_IMAGE", "HOUND_CONFIG_DIR", "HOUND_SECRETS_DIR", "COMPOSE_FILE"];

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
            "scope/access.yaml", "scope/settings.yaml", "deploy/deploy.env", "deploy/compose.override.yml", "deploy/README.md", ".gitignore",
        ];

        expected.Where(f => !File.Exists(Path.Combine(Repo, f))).ShouldBeEmpty();
    }

    [Fact]
    public void Deploy_env_sets_only_documented_non_secret_settings_and_every_one_the_stack_requires()
    {
        var documented = Settings(Path.Combine(RepoRoot, "deploy", "compose", ".env.example"));
        var deploy = Settings(Path.Combine(Repo, "deploy", "deploy.env"));

        deploy.Keys.Except(documented.Keys).ShouldBeEmpty("deploy.env sets only settings .env.example documents");
        RequiredDeploySettings.Except(deploy.Keys).ShouldBeEmpty();
        deploy["CA_VERSION"].ShouldBe(documented["CA_VERSION"], "the template pins the release the stack is tested with");
        deploy.Keys.Where(k => SecretName().IsMatch(k)).ShouldBeEmpty("secrets are files in HOUND_SECRETS_DIR, never settings");
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("deploy/.env")]
    [InlineData("secrets/postgres_password")]
    [InlineData("deploy/secrets/hound_ca_token")]
    [InlineData("m365.pfx")]
    [InlineData("deploy/client.pem")]
    [InlineData("tls.key")]
    [InlineData("caddy-root.crt")]
    [InlineData("evidence/m365/2026-01-01/run.json")]
    [InlineData("exports/backup-report.xlsx")]
    [InlineData("users.csv")]
    public void Gitignore_excludes_secret_and_evidence_paths(string path)
    {
        Ignored(path).ShouldBeTrue($"{path} is ignored");
    }

    [Theory]
    [InlineData("deploy/deploy.env")]
    [InlineData("deploy/compose.override.yml")]
    [InlineData("scope/access.yaml")]
    [InlineData("documents/identity-policy.pdf")]
    public void Gitignore_keeps_the_files_the_repo_holds(string path)
    {
        Ignored(path).ShouldBeFalse($"{path} is kept");
    }

    [Fact]
    public void The_workflow_scans_every_change_for_secrets()
    {
        var workflow = File.ReadAllText(Path.Combine(Repo, ".github", "workflows", "compliance.yml"));

        workflow.ShouldContain("gitleaks/gitleaks-action@");
        workflow.ShouldContain("fetch-depth: 0");
    }

    [Fact]
    public void The_apply_job_applies_the_plan_it_saved()
    {
        var workflow = File.ReadAllText(Path.Combine(Repo, ".github", "workflows", "compliance.yml"));
        var apply = workflow[workflow.IndexOf("\n  apply:", StringComparison.Ordinal)..];

        apply.ShouldContain("scope plan scope/ --config hound.yaml --out \"$RUNNER_TEMP/plan.json\"");
        apply.ShouldContain("scope apply \"$RUNNER_TEMP/plan.json\"");
        apply.ShouldNotContain("scope apply scope/");
    }

    [Fact]
    public void Every_yaml_file_but_the_workflow_and_custom_libraries_links_a_schema_that_exists()
    {
        // A custom library under frameworks/ is CISO Assistant's own format, which has no schema here.
        var files = Directory.GetFiles(Repo, "*.yaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".github", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}frameworks{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

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

    internal static string RepoRoot
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

    /// <summary>Whether the template's own .gitignore ignores a path, as git decides it, with no other ignore rules.</summary>
    private static bool Ignored(string path)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git", ["-c", "core.excludesFile=", "check-ignore", "--no-index", "-q", path])
        {
            WorkingDirectory = Repo,
            RedirectStandardError = true,
        };
        // Only the template's .gitignore applies: GIT_DIR points at a repository of its own, so the parent repo's rules do not.
        var gitDir = Directory.CreateTempSubdirectory("hound-gitignore").FullName;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", ["init", "-q", gitDir]))!.WaitForExit();
        info.Environment["GIT_DIR"] = Path.Combine(gitDir, ".git");
        info.Environment["GIT_WORK_TREE"] = Repo;
        using var git = System.Diagnostics.Process.Start(info)!;
        git.WaitForExit();
        Directory.Delete(gitDir, recursive: true);
        return git.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException($"git check-ignore failed: {git.StandardError.ReadToEnd()}"),
        };
    }

    private static Dictionary<string, string> Settings(string file) =>
        File.ReadLines(file)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);

    private static string[] WithoutSchemaComment(string path) =>
        [.. File.ReadAllLines(path).Where(l => !l.StartsWith("# yaml-language-server:", StringComparison.Ordinal))];

    private Task<int> Hound(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct);

    [GeneratedRegex(@"^# yaml-language-server: \$schema=(?<schema>\S+)$")]
    private static partial Regex SchemaComment();

    [GeneratedRegex("^(?!.*_DIR$).*(PASSWORD|SECRET|TOKEN|_KEY$|CERTIFICATE)")]
    private static partial Regex SecretName();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
