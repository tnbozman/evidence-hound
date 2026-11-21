using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound scope</c> commands: check a compliance repo's scope files, start a new audit file, and plan the scope against CISO Assistant.</summary>
internal static class ScopeCommands
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.scope");
        var dir = new Argument<DirectoryInfo>("scope") { Description = "The scope folder: setup.yaml, controls.yaml and audits/*.yaml." };
        var config = new Option<FileInfo?>("--config") { Description = "A deployment config whose targets must be audits and controls the scope declares." };
        var format = new Option<string>("--format") { Description = "text (the default) or json.", DefaultValueFactory = _ => "text" };
        format.AcceptOnlyFromAmong("text", "json");
        var validate = new Command("validate", "Check the scope files offline and report every problem with its file, line and code.") { dir, config, format };
        validate.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkedScope = await ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            return await ReportAsync(checkedScope.Problems, checkedScope.Summary, parseResult.GetRequiredValue(format), output, logger);
        });

        var framework = new Option<FileInfo>("--framework") { Description = "The framework snapshot (from hound framework snapshot).", Required = true };
        var groups = new Option<string>("--groups") { Description = "The implementation groups the audit assesses, comma-separated, for example 1,2." };
        var refId = new Option<string>("--ref-id") { Description = "The audit's ref_id in CISO Assistant.", Required = true };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the audit file here instead of printing it." };
        var newAudit = new Command("new-audit", "Write a starting audit file listing every in-group requirement, to do and not assessed.") { framework, groups, refId, outFile };
        newAudit.SetAction(async (parseResult, cancellationToken) =>
        {
            var snapshot = await FrameworkCommands.LoadSnapshotAsync(parseResult.GetRequiredValue(framework), cancellationToken);
            return await snapshot.Match(
                s => NewAuditAsync(s, Groups(parseResult.GetValue(groups)), parseResult.GetRequiredValue(refId), parseResult.GetValue(outFile), output, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        var planFormat = new Option<string>("--format") { Description = "markdown (the default, for a pull request comment) or json.", DefaultValueFactory = _ => "markdown" };
        planFormat.AcceptOnlyFromAmong("markdown", "json");
        var prune = new Option<bool>("--prune") { Description = "Also plan deleting the objects hound manages that the scope no longer declares." };
        var plan = new Command(
            "plan",
            $"Show what applying the scope would create, update or delete in CISO Assistant, and what drifted (reads it; needs {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting}). Sends nothing.")
        {
            dir, config, planFormat, prune,
        };
        plan.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkedScope = await ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            if (checkedScope.Problems.Count > 0)
            {
                return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
            }

            return await PublishCommand.Connection(configuration).Match(
                connection => PlanAsync(checkedScope, connection, parseResult.GetValue(prune), parseResult.GetRequiredValue(planFormat), loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan and write nothing." };
        var apply = new Command(
            "apply",
            $"Write the scope to CISO Assistant, in dependency order: libraries, domains, perimeters, systems, audits, documents, controls and rows (needs {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting}). Safe to run again. Give --config so the unlisted rule leaves the rows a mapping computes alone.")
        {
            dir, config, dryRun, prune,
        };
        apply.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkedScope = await ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            if (checkedScope.Problems.Count > 0)
            {
                return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
            }

            return await PublishCommand.Connection(configuration).Match(
                connection => ApplyAsync(checkedScope, connection, parseResult.GetValue(prune), parseResult.GetValue(dryRun), loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        return new Command("scope", "Work with a compliance repo's scope files.") { validate, newAudit, plan, apply };
    }

    private static async Task<int> ApplyAsync(
        CheckedScope checkedScope, CisoAssistantOptions connection, bool prune, bool dryRun, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var declared = ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs);
        var actual = await services.GetRequiredService<IScopeStateSource>().ReadAsync(ScopeStateQuery.For(checkedScope.Scope, declared), cancellationToken);
        return await actual.Match(
            async held =>
            {
                var plan = ScopePlanner.Plan(declared, held, prune);
                if (dryRun)
                {
                    await output.WriteAsync(ScopePlanOutput.Markdown(plan, ScopePeriods.Notes(declared, plan)));
                    return ExitCodes.Ok;
                }

                var report = await ScopeApplier.ApplyAsync(plan, declared, Enum.GetValues<ScopeKind>(), checkedScope.Scope.Setup.RootFolder, services.GetRequiredService<IScopeWriter>(), cancellationToken);
                await output.WriteAsync(ScopePlanOutput.Json(report));
                return report.Failed > 0 ? ExitCodes.PublishFailed : ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> PlanAsync(
        CheckedScope checkedScope, CisoAssistantOptions connection, bool prune, string format, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var declared = ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs);
        var actual = await services.GetRequiredService<IScopeStateSource>().ReadAsync(ScopeStateQuery.For(checkedScope.Scope, declared), cancellationToken);
        return await actual.Match(
            async held =>
            {
                var plan = ScopePlanner.Plan(declared, held, prune);
                var periods = ScopePeriods.Notes(declared, plan);
                await output.WriteAsync(format == "json" ? ScopePlanOutput.Json(plan, periods) : ScopePlanOutput.Markdown(plan, periods));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static string[] Groups(string? groups) =>
        groups is null ? [] : groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<int> NewAuditAsync(FrameworkSnapshot snapshot, string[] groups, string refId, FileInfo? outFile, TextWriter output, CancellationToken cancellationToken)
    {
        var yaml = AuditTemplate.Write(snapshot, groups, refId);
        if (outFile is null)
        {
            await output.WriteAsync(yaml);
            return ExitCodes.Ok;
        }

        await File.WriteAllTextAsync(outFile.FullName, yaml, cancellationToken);
        var count = yaml.Split('\n').Count(line => line.StartsWith("  - ref:", StringComparison.Ordinal) || line.StartsWith("  - urn:", StringComparison.Ordinal));
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"wrote {outFile}: {refId}, {count} requirements"));
        return ExitCodes.Ok;
    }

    /// <summary>A checked scope: its problems, a one-line summary, the parsed files, and what planning it needs from outside them.</summary>
    private sealed record CheckedScope(IReadOnlyList<ScopeProblem> Problems, string Summary, ScopeDocument Scope, ScopeInputs Inputs);

    private static async Task<CheckedScope> ValidateAsync(
        DirectoryInfo dir, FileInfo? configFile, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken)
    {
        var setupPath = Path.Combine(dir.ToString(), "setup.yaml");
        if (!File.Exists(setupPath))
        {
            return new([new ScopeProblem("scope.file_missing", new SourceLine(setupPath, 0), "the scope has no setup.yaml.", "Create setup.yaml with the organisation, frameworks and domains.")], "", new ScopeDocument(new ScopeSetup("Global", [], []), [], []), ScopeInputs.None);
        }

        var controlsPath = Path.Combine(dir.ToString(), "controls.yaml");
        var auditsDir = Path.Combine(dir.ToString(), "audits");
        var audits = Directory.Exists(auditsDir) ? Directory.GetFiles(auditsDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var parsed = ScopeLoader.Parse(
            await ReadAsync(setupPath, cancellationToken),
            File.Exists(controlsPath) ? await ReadAsync(controlsPath, cancellationToken) : null,
            [.. await Task.WhenAll(audits.Select(a => ReadAsync(a, cancellationToken)))]);

        var snapshots = await SnapshotsAsync(parsed.Scope.Setup.Frameworks, Path.GetDirectoryName(Path.GetFullPath(setupPath))!, cancellationToken);
        var targets = configFile is null ? Result.Ok<IReadOnlyList<ScopeTarget>>([]) : await TargetsAsync(configFile, registry, cancellationToken);
        var scope = parsed.Scope;
        var files = await FilesAsync(scope, Path.GetDirectoryName(Path.GetFullPath(dir.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))!, Path.GetDirectoryName(Path.GetFullPath(setupPath))!, cancellationToken);
        IReadOnlyList<ScopeProblem> problems =
        [
            .. parsed.Problems,
            .. snapshots.Problems,
            .. files.Problems,
            .. targets.Match(_ => [], ps => ps.Select(p => new ScopeProblem(p.Code, new SourceLine(configFile!.ToString(), 0), p.Message, "Fix the deployment config first."))),
            .. ScopeValidator.Validate(scope, snapshots.Loaded, targets.Match(t => t, _ => [])),
        ];
        return new(
            problems,
            string.Create(CultureInfo.InvariantCulture, $"scope valid: {scope.Setup.Domains.Count} domain(s), {scope.Controls.Count} control(s), {scope.Audits.Count} audit(s)"),
            scope,
            new ScopeInputs(snapshots.Loaded, configFile is null ? null : targets.Match(t => t, _ => []), files.Found) { Today = today });
    }

    /// <summary>
    /// The files to upload, with their SHA-256: each document's, by path or glob relative to the compliance repo (the folder that holds the
    /// scope folder), and each custom library file, relative to setup.yaml. A path that names no file is a problem.
    /// </summary>
    private static async Task<(Dictionary<string, IReadOnlyList<ScopeFile>> Found, IReadOnlyList<ScopeProblem> Problems)> FilesAsync(
        ScopeDocument scope, string repoDir, string setupDir, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, IReadOnlyList<ScopeFile>>(StringComparer.Ordinal);
        var problems = new List<ScopeProblem>();
        foreach (var document in scope.Audits.SelectMany(a => a.Documents).DistinctBy(d => d.Id))
        {
            var files = new List<ScopeFile>();
            foreach (var pattern in document.Files)
            {
                var matched = Matched(repoDir, pattern);
                if (matched.Length == 0)
                {
                    problems.Add(new ScopeProblem("scope.document_file_missing", document.At, $"document '{document.Id}' names {pattern}, which matches no file.", "Add the file to the compliance repo, or fix the path: it is relative to the folder that holds scope/."));
                }

                foreach (var path in matched)
                {
                    files.Add(new ScopeFile(path, await Sha256Async(path, cancellationToken)));
                }
            }

            found[document.Id] = files;
        }

        foreach (var framework in scope.Setup.Frameworks.Where(f => f.LibraryFile is not null))
        {
            var path = Path.GetFullPath(framework.LibraryFile!, setupDir);
            if (File.Exists(path))
            {
                found[framework.Urn] = [new ScopeFile(path, await Sha256Async(path, cancellationToken))];
            }
        }

        return (found, problems);
    }

    private static string[] Matched(string baseDir, string pattern)
    {
        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            var path = Path.GetFullPath(pattern, baseDir);
            return File.Exists(path) ? [path] : [];
        }

        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);
        return [.. matcher.GetResultsInFullPath(baseDir).Order(StringComparer.Ordinal)];
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<ScopeSource> ReadAsync(string path, CancellationToken cancellationToken) =>
        new(path, await File.ReadAllTextAsync(path, cancellationToken));

    /// <summary>Each framework's snapshot, from its <c>snapshot</c> file or its library <c>file</c>, relative to setup.yaml.</summary>
    private static async Task<(Dictionary<string, FrameworkSnapshot> Loaded, IReadOnlyList<ScopeProblem> Problems)> SnapshotsAsync(
        IReadOnlyList<ScopeFramework> frameworks, string baseDir, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<string, FrameworkSnapshot>(StringComparer.Ordinal);
        var problems = new List<ScopeProblem>();
        foreach (var framework in frameworks)
        {
            var (path, read) = framework switch
            {
                { Snapshot: { } snapshot } => (snapshot, (Func<string, string, Result<FrameworkSnapshot>>)FrameworkSnapshotJson.Read),
                { LibraryFile: { } library } => (library, FrameworkLibraryReader.Snapshot),
                _ => ((string?)null, (Func<string, string, Result<FrameworkSnapshot>>?)null),
            };
            if (path is null || read is null)
            {
                continue;
            }

            var full = Path.GetFullPath(path, baseDir);
            var result = File.Exists(full)
                ? read(await File.ReadAllTextAsync(full, cancellationToken), path)
                : Result.Fail<FrameworkSnapshot>(new Problem("scope.framework_file_missing", $"{path} does not exist."));
            result.Match(
                snapshot =>
                {
                    if (snapshot.FrameworkUrn == framework.Urn)
                    {
                        loaded[framework.Urn] = snapshot;
                    }
                    else
                    {
                        problems.Add(new ScopeProblem("scope.framework_snapshot_mismatch", framework.At, $"{path} is framework '{snapshot.FrameworkUrn}', not '{framework.Urn}'.", "Point 'snapshot' at this framework's snapshot."));
                    }

                    return 0;
                },
                ps =>
                {
                    problems.AddRange(ps.Select(p => new ScopeProblem(p.Code, framework.At, p.Message, "Make the snapshot again with 'hound framework snapshot <library>'.")));
                    return 0;
                });
        }

        return (loaded, problems);
    }

    /// <summary>The deployment's targets: each audit, the requirements its mapping computes, and every control it names.</summary>
    private static async Task<Result<IReadOnlyList<ScopeTarget>>> TargetsAsync(FileInfo configFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var config = await ConfigCommands.LoadAsync(configFile, registry, cancellationToken);
        return await config.Match(
            async c => (await PlanCommand.LoadMappingsAsync(c.Targets, cancellationToken)).Map(loaded => (IReadOnlyList<ScopeTarget>)
            [
                .. loaded.Select(t => new ScopeTarget(
                    t.Target.ComplianceAssessmentRefId,
                    [.. t.Mapping.Requirements.Select(r => r.Key)],
                    [.. t.Target.Controls.Values.Append(t.Target.HealthControlRefId).OfType<string>().Distinct(StringComparer.Ordinal)],
                    new SourceLine(configFile.ToString(), 0))),
            ]),
            problems => Task.FromResult(Result.Fail<IReadOnlyList<ScopeTarget>>(problems)));
    }

    private static async Task<int> ReportAsync(IReadOnlyList<ScopeProblem> problems, string summary, string format, TextWriter output, ILogger logger)
    {
        if (format == "json")
        {
            var json = new JsonArray([.. problems.Select(p => new JsonObject
            {
                ["code"] = p.Code,
                ["file"] = p.At.File,
                ["line"] = p.At.Line,
                ["message"] = p.Message,
                ["hint"] = p.Hint,
            })]);
            await output.WriteLineAsync(json.ToJsonString(Indented));
            return problems.Count == 0 ? ExitCodes.Ok : ExitCodes.Configuration;
        }

        if (problems.Count == 0)
        {
            await output.WriteLineAsync(summary);
            return ExitCodes.Ok;
        }

        return Log.Problems(logger, problems.Select(p => new Problem(p.Code, $"{Where(p.At)}: {p.Message} [{p.Code}] {p.Hint}")));
    }

    private static string Where(SourceLine at) =>
        at.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"{at.File} line {at.Line}") : at.File;
}
