using System.Text.RegularExpressions;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
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

    internal static string Repo => Path.Combine(RepoRoot, "examples", "compliance-repo");

    /// <summary>s09-32: what the onboarding tutorial adds to a copy of the template, at the same paths.</summary>
    internal static string Tutorial => Path.Combine(RepoRoot, "examples", "onboarding-tutorial");

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
    public void Scope_guide_documents_the_ref_id_conventions_once_and_the_examples_follow_them()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot, "docs", "scope.md"));
        foreach (var convention in RefIds.Table.Where(c => c.Prefix.Length > 0))
        {
            guide.Split($"| {convention.Name} | `{convention.Prefix}` | `{convention.Example}` |").Length.ShouldBe(2, convention.Name);
        }

        guide.ShouldContain("`scope.ref_id_convention`");

        // Every ref ID declared in the example repo and the tutorial's files follows its kind's convention.
        string[] folders = [Path.Combine(Repo, "scope"), Path.Combine(Tutorial, "scope")];
        (string Key, RefIdKind Kind)[] nested = [("solutions", RefIdKind.Solution), ("contracts", RefIdKind.Contract), ("assessments", RefIdKind.EntityAssessment)];
        foreach (var file in folders.SelectMany(f => Directory.GetFiles(f, "*.yaml", SearchOption.AllDirectories)))
        {
            var kindOfTop = file.Contains("third-parties", StringComparison.Ordinal) ? RefIdKind.Supplier : file.Contains("audits", StringComparison.Ordinal) ? RefIdKind.Audit : (RefIdKind?)null;
            var section = "";
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains(':', StringComparison.Ordinal) && !line.StartsWith("ref_id", StringComparison.Ordinal))
                {
                    section = line.Split(':')[0];
                }

                if (line.TrimStart().TrimStart('-', ' ').StartsWith("ref_id:", StringComparison.Ordinal))
                {
                    var refId = line.Split(':', 2)[1].Trim().Trim('"');
                    var kind = !char.IsWhiteSpace(line[0]) ? kindOfTop
                        : kindOfTop == RefIdKind.Supplier ? nested.Where(n => n.Key == section).Select(n => (RefIdKind?)n.Kind).FirstOrDefault()
                        : kindOfTop == RefIdKind.Audit ? RefIdKind.Control
                        : file.EndsWith("controls.yaml", StringComparison.Ordinal) ? RefIdKind.Control
                        : null;
                    if (kind is { } known)
                    {
                        RefIds.Follows(known, refId).ShouldBeTrue($"{file}: {refId}");
                    }
                }
            }
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
    [InlineData("plan.json")]
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

    /// <summary>s09-32: the template starts without the tutorial's systems and suppliers, so a new repo applies in one go.</summary>
    [Fact]
    public void The_template_holds_none_of_the_tutorials_systems()
    {
        File.ReadAllText(Path.Combine(Repo, "scope", "setup.yaml")).ShouldNotContain("GRC-PLATFORM");
        Directory.Exists(Path.Combine(Repo, "scope", "third-parties")).ShouldBeFalse();
        Directory.Exists(Path.Combine(Repo, "onboarding")).ShouldBeFalse();
    }

    /// <summary>s09-32: the onboarding tutorial's two systems, each supplied under an active contract, and the open-source projects the platform runs.</summary>
    [Fact]
    public async Task Tutorial_systems_and_suppliers_are_declared()
    {
        var repo = TutorialRepo();
        try
        {
            var scope = await ScopeAsync(repo);

            var systems = scope.Setup.Domains.SelectMany(d => d.Systems).ToDictionary(s => s.Name, StringComparer.Ordinal);
            systems["GRC-PLATFORM"].Solution?.Value.ShouldBe("SOL-OCI-HOSTING");
            systems["OFFICE-FIREWALL"].Solution?.Value.ShouldBe("SOL-SOPHOS-FIREWALL");
            systems["GRC-PLATFORM"].Owner.ShouldBe("it@example.com");

            var suppliers = scope.ThirdParties.ToDictionary(e => e.RefId, StringComparer.Ordinal);
            suppliers.Keys.ShouldBe(["SUP-CISO-ASSISTANT", "SUP-EVIDENCE-HOUND", "SUP-ORACLE", "SUP-SOPHOS"], ignoreOrder: true);
            foreach (var (supplier, solution) in new[] { ("SUP-ORACLE", "SOL-OCI-HOSTING"), ("SUP-SOPHOS", "SOL-SOPHOS-FIREWALL") })
            {
                var entity = suppliers[supplier];
                entity.Contracts.ShouldContain(c => c.Status == "active" && c.Solutions.Any(s => s.Value == solution), $"{supplier} supplies {solution} under an active contract");
                new[] { entity.Dependency, entity.Penetration, entity.Maturity, entity.Trust }.ShouldAllBe(level => level != null, $"{supplier}'s levels are decided in review");
                entity.Assessments.ShouldNotBeEmpty($"{supplier} has a review for the GRC team to conclude");
            }

            foreach (var project in new[] { "SUP-CISO-ASSISTANT", "SUP-EVIDENCE-HOUND" })
            {
                suppliers[project].Solutions.ShouldNotBeEmpty();
                suppliers[project].Contracts.ShouldAllBe(c => c.AnnualExpense == null, $"{project} is open source, with no contract value");
            }
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    /// <summary>s09-32: the tutorial's audits validate, every not applicable row says why and who approved it or which system provides it, and API checks are left to the collectors.</summary>
    [Fact]
    public async Task Tutorial_audits_validate_with_justified_not_applicable_rows()
    {
        var repo = TutorialRepo();
        try
        {
            (await Hound("config", "validate", Path.Combine(repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString());
            (await Hound("scope", "validate", Path.Combine(repo, "scope"), "--config", Path.Combine(repo, "hound.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString());
            (await Hound("mapping", "validate", Path.Combine(repo, "mappings", "office-firewall-essential-eight-2023.yaml"))).ShouldBe(ExitCodes.Ok, _error.ToString());
            var audits = (await ScopeAsync(repo)).Audits.ToDictionary(a => a.RefId, StringComparer.Ordinal);

            foreach (var (audit, system) in new[] { ("GRC-PLATFORM-ESSENTIAL-EIGHT-2023", "GRC-PLATFORM"), ("OFFICE-FIREWALL-ESSENTIAL-EIGHT-2023", "OFFICE-FIREWALL") })
            {
                var rows = audits[audit].Assessments;
                audits[audit].Systems.Select(s => s.Value).ShouldBe([system]);
                audits[audit].Groups.Select(g => g.Value).ShouldBe(["1", "2"]);
                rows.Where(r => r.Result == ComplianceResult.NotApplicable).ShouldNotBeEmpty();
                rows.Where(r => r.Result == ComplianceResult.NotApplicable)
                    .Where(r => !(r.Observation.StartsWith("Not applicable: ", StringComparison.Ordinal) && r.Observation.Contains("Approved by ", StringComparison.Ordinal))
                        && !r.Observation.StartsWith("Provided by ", StringComparison.Ordinal))
                    .Select(r => r.Requirement.Key.Value)
                    .ShouldBeEmpty($"each not applicable row of {audit} has a justification and approver, or names the system that provides it");
            }

            var platform = audits["GRC-PLATFORM-ESSENTIAL-EIGHT-2023"].Assessments;
            Row(platform, "ISM-0843").Result.ShouldBe(ComplianceResult.NotApplicable, "application control does not apply to a private Linux server");
            Row(platform, "ISM-1671").Result.ShouldBe(ComplianceResult.NotApplicable, "there are no Office macros on a Linux container host");
            Row(platform, "ISM-1511").Observation.ShouldStartWith("Manual evidence: ", customMessage: "backups are manual evidence");
            Row(platform, "ISM-1695").Observation.ShouldStartWith("Manual evidence: ", customMessage: "patching is manual evidence");
            Row(platform, "ISM-1173").Observation.ShouldStartWith("Met by Example 365", customMessage: "admin MFA is the identity provider's");
            Row(platform, "ISM-1173").Result.ShouldBe(ComplianceResult.NotAssessed, "met by another system stays open until that system's audit shows it met");
            Row(platform, "ISM-1173").ProvidedBy.ShouldBe(["Example 365"]);

            var firewall = audits["OFFICE-FIREWALL-ESSENTIAL-EIGHT-2023"].Assessments.Select(r => r.Requirement.Key.Value).ToArray();
            firewall.ShouldNotContain("ISM-1501", "the firmware check comes from the sophos-central profile");
            firewall.ShouldNotContain("ISM-1883", "the Super Admin check comes from the sophos-central profile");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    /// <summary>s09-32: the firewall's audit is fed by the sophos-central profile, and the platform's cloud is checked for drift.</summary>
    [Fact]
    public async Task Tutorial_collectors_feed_the_firewall_audit_and_check_the_platform_for_drift()
    {
        var config = await File.ReadAllTextAsync(Path.Combine(Tutorial, "hound.yaml"), _ct);

        config.ShouldContain("profile: sophos-central");
        config.ShouldContain("profile: terraform-drift");
        config.ShouldContain("compliance_assessment: OFFICE-FIREWALL-ESSENTIAL-EIGHT-2023");
        var mapping = await File.ReadAllTextAsync(Path.Combine(Tutorial, "mappings", "office-firewall-essential-eight-2023.yaml"), _ct);
        mapping.ShouldContain("network.firewall.firmware_supported");
        mapping.ShouldContain("network.management.super_admin_count");
        var compose = await File.ReadAllTextAsync(Path.Combine(Tutorial, "deploy", "compose.override.yml"), _ct);
        compose.ShouldContain("sophos_client_secret");
        compose.ShouldContain("iac_actions_token");
    }

    /// <summary>s09-51: each per-control check in the tutorial's copy of terraform-tests names a run block of the control tests.</summary>
    [Fact]
    public async Task Grc_infra_control_checks_name_tests_that_exist()
    {
        var profile = await File.ReadAllTextAsync(Path.Combine(Tutorial, "profiles", "grc-infra-controls.yaml"), _ct);
        var tests = await File.ReadAllTextAsync(Path.Combine(Tutorial, "infra", "oci", "tests", "controls.tftest.hcl"), _ct);
        var runs = RunBlock().Matches(tests).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var named = TestSetting().Matches(profile).Select(m => m.Groups["name"].Value).ToArray();

        named.ShouldNotBeEmpty();
        named.Where(n => !runs.Contains(n)).ShouldBeEmpty("every check names a run block in controls.tftest.hcl");
        (await Hound("profile", "test", "grc-infra-controls", "--profiles", Path.Combine(Tutorial, "profiles"))).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    /// <summary>s09-51: a backup row the bucket's settings answer passes only when its control test and the drift check both pass.</summary>
    [Fact]
    public async Task Grc_platform_backup_rows_need_the_control_test_and_no_drift()
    {
        var path = Path.Combine(Tutorial, "mappings", "grc-platform-essential-eight-2023.yaml");
        (await Hound("mapping", "validate", path)).ShouldBe(ExitCodes.Ok, _error.ToString());
        var mapping = await File.ReadAllTextAsync(path, _ct);
        foreach (var refId in new[] { "ISM-1811", "ISM-1814", "ISM-1707", "ISM-1812", "ISM-1705" })
        {
            var row = Regex.Match(mapping, $@"- ref: {refId}\n(?<body>(?:    .*\n)+)").Groups["body"].Value;
            row.ShouldContain("rule: all_pass", customMessage: refId);
            row.ShouldContain("infrastructure.drift.none", customMessage: refId);
            row.ShouldMatch(@"infrastructure\.controls\.\w+", customMessage: refId);
        }

        var config = await File.ReadAllTextAsync(Path.Combine(Tutorial, "hound.yaml"), _ct);
        config.ShouldContain("profile: grc-infra-controls");
        config.ShouldContain("compliance_assessment: GRC-PLATFORM-ESSENTIAL-EIGHT-2023");
        var repo = TutorialRepo();
        try
        {
            var platform = (await ScopeAsync(repo)).Audits.Single(a => a.RefId == "GRC-PLATFORM-ESSENTIAL-EIGHT-2023").Assessments.Select(r => r.Requirement.Key.Value).ToArray();
            platform.ShouldNotContain("ISM-1811", "the bucket's settings come from the control tests and the drift check");
            platform.ShouldContain("ISM-1515", "the restore test stays manual evidence");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [GeneratedRegex(@"^run ""(?<name>\w+)""", RegexOptions.Multiline)]
    private static partial Regex RunBlock();

    [GeneratedRegex(@"test: ""?(?<name>\w+)""?")]
    private static partial Regex TestSetting();

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

    private static HandAssessment Row(IReadOnlyList<HandAssessment> rows, string refId) => rows.Single(r => r.Requirement.Key.Value == refId);

    /// <summary>A copy of the template with the tutorial's files over it, as the tutorial leaves it.</summary>
    internal static string TutorialRepo()
    {
        var repo = Directory.CreateTempSubdirectory("hound-tutorial-repo").FullName;
        CopyFolder(Repo, repo);
        CopyFolder(Tutorial, repo);
        return repo;
    }

    internal static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }
    }

    /// <summary>A repo's whole scope, as <c>hound scope validate</c> reads it.</summary>
    private static async Task<ScopeDocument> ScopeAsync(string repo)
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Path.Combine(repo, "scope");
        async Task<ScopeSource> Source(string path) => new(path, await File.ReadAllTextAsync(path, ct));
        var audits = await Task.WhenAll(Directory.GetFiles(Path.Combine(scope, "audits"), "*.yaml").Order(StringComparer.Ordinal).Select(Source));
        var thirdParties = await Task.WhenAll(Directory.GetFiles(Path.Combine(scope, "third-parties"), "*.yaml").Order(StringComparer.Ordinal).Select(Source));
        var parsed = ScopeLoader.Parse(
            await Source(Path.Combine(scope, "setup.yaml")), await Source(Path.Combine(scope, "controls.yaml")), audits,
            await Source(Path.Combine(scope, "access.yaml")), await Source(Path.Combine(scope, "settings.yaml")), thirdParties);
        parsed.Problems.ShouldBeEmpty();
        return parsed.Scope;
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
