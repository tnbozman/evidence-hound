using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Risk;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound scope</c> commands: check a compliance repo's scope files, start a new audit file, and plan the scope against CISO Assistant.</summary>
internal static class ScopeCommands
{

    /// <summary>With <c>--detailed-exitcode</c>, the exit code of a plan that changes something or found drift.</summary>
    internal const int PlanHasChanges = 2;

    internal static readonly string DetailedExitCodeFlag = "--detailed-exitcode";

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.scope");
        var dir = new Argument<DirectoryInfo>("scope") { Description = "The scope folder: setup.yaml, controls.yaml and audits/*.yaml." };
        var config = new Option<FileInfo?>("--config") { Description = "A deployment config whose targets must be audits and controls the scope declares." };
        var format = new Option<string>("--format") { Description = "text (the default) or json.", DefaultValueFactory = _ => "text" };
        format.AcceptOnlyFromAmong("text", "json");
        var validateJson = new Option<bool>("--json") { Description = "The same as --format json, for an agent." };
        var validate = new Command("validate", "Check the scope files offline and report every problem with its file, line and code.") { dir, config, format, validateJson };
        validate.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkedScope = await RepoScope.ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            return await ReportAsync(checkedScope.Problems, checkedScope.Summary, parseResult.GetValue(validateJson) ? "json" : parseResult.GetRequiredValue(format), output, logger, checkedScope.Warnings);
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
        var planJson = new Option<bool>("--json") { Description = "The same as --format json, for an agent." };
        var prune = new Option<bool>("--prune") { Description = "Also plan deleting the objects hound manages that the scope no longer declares." };
        var showUnmanaged = new Option<bool>("--show-unmanaged") { Description = "Also list the feature flags and general settings settings.yaml does not name, with their values." };
        var detailedExitCode = new Option<bool>(DetailedExitCodeFlag) { Description = "Exit 0 when there is nothing to change and no drift, 2 when there is, and 1 on any error." };
        var planOut = new Option<FileInfo?>("--out") { Description = "Also save the plan here, for 'hound scope apply <file>' to apply exactly. Holds no token; do not commit it." };
        var plan = new Command(
            "plan",
            $"Show what applying the scope would create, update or delete in CISO Assistant, and what drifted (reads it; with --config, uses the config's ciso_assistant address and token unless {CisoAssistantConnection.UrlSetting} and {CisoAssistantConnection.TokenSetting} override them; without it, needs both). Sends nothing.")
        {
            dir, config, planFormat, planJson, prune, showUnmanaged, detailedExitCode, planOut,
        };
        plan.SetAction(async (parseResult, cancellationToken) =>
        {
            var detailed = parseResult.GetValue(detailedExitCode);
            var checkedScope = await RepoScope.ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            if (!parseResult.GetValue(planJson) && parseResult.GetRequiredValue(planFormat) != "json")
            {
                await WarnAsync(checkedScope.Warnings, output);
            }

            var exitCode = checkedScope.Problems.Count > 0
                ? await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger)
                : await (await CisoAssistantConnection.ResolveAsync(configuration, parseResult.GetValue(config), registry, cancellationToken)).Match(
                    connection => PlanAsync(
                        checkedScope,
                        connection,
                        new PlanOptions(parseResult.GetValue(prune), parseResult.GetValue(showUnmanaged), parseResult.GetValue(planJson) ? "json" : parseResult.GetRequiredValue(planFormat))
                        {
                            DetailedExitCode = detailed,
                            Save = parseResult.GetValue(planOut) is { } file ? new PlanSave(file, parseResult.GetRequiredValue(dir), parseResult.GetValue(config)) : null,
                        },
                        loggers,
                        output,
                        logger,
                        cancellationToken),
                    problems => Task.FromResult(Log.Problems(logger, problems)));
            return detailed && exitCode is not (ExitCodes.Ok or PlanHasChanges) ? ExitCodes.Unexpected : exitCode;
        });

        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan and write nothing." };
        var scopeOrPlan = new Argument<string>("scope") { Description = "The scope folder, or a plan saved with 'hound scope plan --out' to apply exactly as reviewed." };
        var apply = new Command(
            "apply",
            $"Write the scope to CISO Assistant, in dependency order: libraries, domains, perimeters, systems, audits, documents, controls and rows (with --config, uses the config's ciso_assistant address and token unless {CisoAssistantConnection.UrlSetting} and {CisoAssistantConnection.TokenSetting} override them; without it, needs both). Safe to run again. Give --config so the unlisted rule leaves the rows a mapping computes alone. Given a saved plan, applies it without planning again, and refuses it if the scope or CISO Assistant changed since.")
        {
            scopeOrPlan, config, dryRun, prune,
        };
        apply.SetAction(async (parseResult, cancellationToken) =>
        {
            var target = parseResult.GetRequiredValue(scopeOrPlan);
            if (File.Exists(target))
            {
                return await CisoAssistantConnection.FromSettings(configuration).Match(
                    connection => ApplySavedAsync(target, connection, parseResult.GetValue(dryRun), registry, loggers, output, logger, cancellationToken),
                    problems => Task.FromResult(Log.Problems(logger, problems)));
            }

            var checkedScope = await RepoScope.ValidateAsync(new DirectoryInfo(target), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            await WarnAsync(checkedScope.Warnings, output);
            if (checkedScope.Problems.Count > 0)
            {
                return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
            }

            return await (await CisoAssistantConnection.ResolveAsync(configuration, parseResult.GetValue(config), registry, cancellationToken)).Match(
                connection => ApplyAsync(checkedScope, connection, parseResult.GetValue(prune), parseResult.GetValue(dryRun), loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        var outDir = new Option<DirectoryInfo>("--out") { Description = "The scope folder to write; documents go next to it, in the compliance repo.", Required = true };
        var rootFolder = new Option<string>("--root-folder") { Description = "The folder the domains sit under (default Global).", DefaultValueFactory = _ => "Global" };
        var withFiles = new Option<bool>("--with-files") { Description = "Also download each document's file into documents/<id>/ in the compliance repo." };
        var export = new Command(
            "export",
            $"Write what CISO Assistant holds under the root folder as scope files: setup, controls, one file per audit and framework snapshots (reads it; needs {CisoAssistantConnection.UrlSetting} and {CisoAssistantConnection.TokenSetting}). Results runs compute and their evidence are left out.")
        {
            outDir, rootFolder, withFiles,
        };
        export.SetAction((parseResult, cancellationToken) => CisoAssistantConnection.FromSettings(configuration).Match(
            connection => ExportAsync(parseResult.GetRequiredValue(outDir), parseResult.GetRequiredValue(rootFolder), parseResult.GetValue(withFiles), connection, loggers, output, logger, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems))));

        return new Command("scope", "Work with a compliance repo's scope files.") { validate, newAudit, plan, apply, export, ScopeImportCommand.Build(output, logger) };
    }

    /// <summary>For the review session: shows the plan for the scope folder, and applies it once <paramref name="confirm"/> says so.</summary>
    internal static async Task<int> PlanThenApplyAsync(
        DirectoryInfo scope, FileInfo? config, IConfiguration configuration, CollectorRegistry registry, ILoggerFactory loggers, TextWriter output,
        Func<CancellationToken, Task<bool>> confirm, CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger("hound.scope");
        var checkedScope = await RepoScope.ValidateAsync(scope, config, registry, AuditSeries.Today(configuration), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
        }

        return await (await CisoAssistantConnection.ResolveAsync(configuration, config, registry, cancellationToken)).Match(
            async connection =>
            {
                var planned = await ApplyAsync(checkedScope, connection, prune: false, dryRun: true, loggers, output, logger, cancellationToken);
                return planned != ExitCodes.Ok || !await confirm(cancellationToken)
                    ? planned
                    : await ApplyAsync(checkedScope, connection, prune: false, dryRun: false, loggers, output, logger, cancellationToken);
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
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
                var suppliers = Suppliers(checkedScope, held);
                IReadOnlyList<ScopeProblem> refused = [.. suppliers.Refused, .. LibraryValidation.Refused(declared, held)];
                if (refused.Count > 0)
                {
                    return await ReportAsync(refused, "", "text", output, logger);
                }

                var plan = ScopePlanner.Plan(declared, held, prune);
                var compliance = ComplianceImpact.Of(plan, declared, held, checkedScope.Inputs);
                if (dryRun)
                {
                    await output.WriteAsync(ScopePlanOutput.Markdown(plan, PlanNotes.None with
                    {
                        Periods = ScopePeriods.Notes(declared, plan),
                        Compliance = compliance,
                        Suppliers = suppliers.Notes,
                        Exceptions = ExceptionReview.Notes(held, checkedScope.Inputs.Today ?? DateOnly.FromDateTime(DateTime.UtcNow)),
                        Gaps = CapabilityMap.Gaps(checkedScope.Scope, checkedScope.Inputs.Snapshots),
                        Dependencies = OpenDependencies.Of(checkedScope.Scope, checkedScope.Inputs.Targets ?? []),
                    }));
                    return ExitCodes.Ok;
                }

                var report = await ScopeApplier.ApplyAsync(plan, declared, Enum.GetValues<ScopeKind>(), checkedScope.Scope.Setup.RootFolder, services.GetRequiredService<IScopeWriter>(), cancellationToken);
                await output.WriteAsync(ScopePlanOutput.Json(report, compliance));
                return report.Failed > 0 ? ExitCodes.PublishFailed : ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>
    /// Applies a saved plan exactly, without planning again. Refuses it (<c>scope.plan_stale</c>) when it was made against another CISO Assistant,
    /// when the scope folder, config or document files changed since, or when an object the plan reads changed in CISO Assistant since.
    /// </summary>
    private static async Task<int> ApplySavedAsync(
        string file, CisoAssistantOptions connection, bool dryRun, CollectorRegistry registry, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var read = SavedPlanFile.Read(await File.ReadAllTextAsync(file, cancellationToken), file);
        if (read is Result<SavedPlan>.Fail failed)
        {
            return Log.Problems(logger, failed.Errors);
        }

        var saved = read.Match(s => s, _ => throw new InvalidOperationException());
        Problem Stale(string why) => new("scope.plan_stale", $"{file} is stale: {why}. Plan again with 'hound scope plan --out', review it, and apply that. [scope.plan_stale]");
        if (saved.CisoAssistantUrl != connection.ApiUrl.ToString())
        {
            return Log.Problems(logger, [Stale($"it was made against {saved.CisoAssistantUrl}, not {connection.ApiUrl}")]);
        }

        var scopeDir = new DirectoryInfo(saved.Scope);
        var config = saved.Config is null ? null : new FileInfo(saved.Config);
        var checkedScope = await RepoScope.ValidateAsync(scopeDir, config, registry, DateOnly.ParseExact(saved.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
        }

        if (await InputsSha256Async(scopeDir, config, checkedScope.Inputs, cancellationToken) != saved.InputsSha256)
        {
            return Log.Problems(logger, [Stale($"the files in {saved.Scope}{(saved.Config is null ? "" : $", {saved.Config}")} or the documents they name changed since it was made")]);
        }

        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var declared = ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs);
        var actual = await services.GetRequiredService<IScopeStateSource>().ReadAsync(ScopeStateQuery.For(checkedScope.Scope, declared), cancellationToken);
        return await actual.Match(
            async held =>
            {
                var now = Fingerprints(saved.Plan, held, saved.Read.Keys);
                var changed = saved.Read.Where(r => now.GetValueOrDefault(r.Key) != r.Value).Select(r => r.Key).ToArray();
                if (changed.Length > 0)
                {
                    return Log.Problems(logger, [Stale($"CISO Assistant changed since it was made: {string.Join(", ", changed)}")]);
                }

                if (Suppliers(checkedScope, held).Refused is { Count: > 0 } refused)
                {
                    return await ReportAsync(refused, "", "text", output, logger);
                }

                if (dryRun)
                {
                    await output.WriteAsync(ScopePlanOutput.Markdown(saved.Plan, PlanNotes.None with { Compliance = saved.Compliance }));
                    return ExitCodes.Ok;
                }

                var report = await ScopeApplier.ApplyAsync(saved.Plan, declared, Enum.GetValues<ScopeKind>(), saved.RootFolder, services.GetRequiredService<IScopeWriter>(), cancellationToken);
                await output.WriteAsync(ScopePlanOutput.Json(report, saved.Compliance));
                return report.Failed > 0 ? ExitCodes.PublishFailed : ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>
    /// For each object the plan writes or deletes, by kind and key, the fingerprint of the fields CISO Assistant holds for it, or null when
    /// it holds none. With <paramref name="keys"/>, those objects instead.
    /// </summary>
    private static Dictionary<string, string?> Fingerprints(ScopePlan plan, IReadOnlyList<ActualObject> held, IEnumerable<string>? keys = null)
    {
        var byKey = held.GroupBy(h => ReadKey(h.Held.Kind, h.Held.Key), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Held.Fields, StringComparer.Ordinal);
        return (keys ?? plan.Changes.Select(c => ReadKey(c.Kind, c.Key))).Distinct(StringComparer.Ordinal)
            .ToDictionary(k => k, k => byKey.GetValueOrDefault(k) is { } fields ? ScopeMarker.Fingerprint(fields) : null, StringComparer.Ordinal);
    }

    private static string ReadKey(ScopeKind kind, string key) => $"{ScopePlanOutput.Kind(kind)}/{key}";

    /// <summary>A SHA-256 over the scope folder's files, the config and the documents' files, by content: what a saved plan was made from.</summary>
    private static async Task<string> InputsSha256Async(DirectoryInfo scopeDir, FileInfo? config, ScopeInputs inputs, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var root = Path.GetFullPath(scopeDir.FullName);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(p => (Full: p, Relative: Path.GetRelativePath(root, p).Replace('\\', '/'))).OrderBy(p => p.Relative, StringComparer.Ordinal))
        {
            lines.Add($"scope/{path.Relative} {await RepoScope.Sha256Async(path.Full, cancellationToken)}");
        }

        if (config is not null && config.Exists)
        {
            lines.Add($"config {await RepoScope.Sha256Async(config.FullName, cancellationToken)}");
        }

        lines.AddRange(inputs.Files.OrderBy(f => f.Key, StringComparer.Ordinal).SelectMany(f => f.Value.Select(file => $"file {f.Key} {file.Sha256}")));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static async Task<int> ExportAsync(
        DirectoryInfo outDir, string rootFolder, bool withFiles, CisoAssistantOptions connection, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var source = services.GetRequiredService<IScopeExportSource>();
        var state = await source.ReadAllAsync(rootFolder, cancellationToken);
        return await state.Match(
            async held =>
            {
                var scopeDir = Path.GetFullPath(outDir.FullName);
                var repoDir = Path.GetDirectoryName(scopeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
                var documentFiles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                if (withFiles)
                {
                    var documents = held.Objects.Where(o => o.Held.Kind == ScopeKind.Document && o.Held.Fields.ContainsKey("domain")).Select(o => o.Held.Key).Distinct(StringComparer.Ordinal);
                    foreach (var id in documents)
                    {
                        var downloaded = await source.DownloadAsync(id, cancellationToken);
                        foreach (var file in downloaded.Match(f => f, problems => { Log.Problems(logger, problems); return []; }))
                        {
                            var relative = $"documents/{id}/{file.FileName}";
                            var path = Path.Combine(repoDir, "documents", id, file.FileName);
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            await File.WriteAllBytesAsync(path, file.Content, cancellationToken);
                            documentFiles[id] = [relative];
                        }
                    }
                }

                var files = ScopeExport.Write(held, documentFiles);
                foreach (var (relative, text) in files)
                {
                    var path = Path.Combine(scopeDir, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, text, cancellationToken);
                }

                var count = (ScopeKind kind) => held.Objects.Count(o => o.Held.Kind == kind);
                await output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"exported to {scopeDir}: {count(ScopeKind.Domain)} domain(s), {count(ScopeKind.Control)} control(s), {files.Keys.Count(k => k.StartsWith("audits/", StringComparison.Ordinal))} audit(s), {documentFiles.Count} file(s)"));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>How to plan: whether to prune, whether to list unmanaged settings, the output format, the exit codes, and where to save the plan.</summary>
    private sealed record PlanOptions(bool Prune, bool ShowUnmanaged, string Format)
    {
        public bool DetailedExitCode { get; init; }

        public PlanSave? Save { get; init; }
    }

    /// <summary>Where to save a plan, and the scope folder and config it is made from, as given.</summary>
    private sealed record PlanSave(FileInfo File, DirectoryInfo Scope, FileInfo? Config);

    /// <summary>Where <c>hound review prepare</c> saves the plan the person's session applies; <c>*.plan.json</c> is never committed.</summary>
    internal static string PreparedPlan(string repo) => Path.Combine(repo, "review", "prepared.plan.json");

    /// <summary>
    /// Plans the scope and saves it to <paramref name="file"/> for the person's session (s18-02). Reads only. Returns the number of changes
    /// and the file, or no file and why: nothing to apply, or the scope could not be planned.
    /// </summary>
    internal static async Task<(int Changes, string? Saved, string Note)> PrepareAsync(
        DirectoryInfo scope, FileInfo? config, CisoAssistantOptions connection, string file, DateOnly today, CollectorRegistry registry, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var checkedScope = await RepoScope.ValidateAsync(scope, config, registry, today, cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return (0, null, string.Create(CultureInfo.InvariantCulture, $"The scope has {checkedScope.Problems.Count} problem(s); fix them first: hound scope validate scope --config hound.yaml --json."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var options = new PlanOptions(Prune: false, ShowUnmanaged: false, "json") { Save = new PlanSave(new FileInfo(file), scope, config) };
        var exit = await PlanAsync(checkedScope, connection, options, loggers, TextWriter.Null, loggers.CreateLogger("hound.scope"), cancellationToken);
        if (exit != ExitCodes.Ok || !File.Exists(file) || SavedPlanFile.Read(await File.ReadAllTextAsync(file, cancellationToken), file) is not Result<SavedPlan>.Ok { Value: var saved })
        {
            return (0, null, "Could not plan the scope; run hound scope plan scope --config hound.yaml to see why.");
        }

        if (saved.Plan.IsEmpty)
        {
            File.Delete(file);
            return (0, null, "Nothing to apply: CISO Assistant already holds the scope.");
        }

        return (saved.Plan.Changes.Count, file, "Saved for the person's session, which applies it after the accepted items, or refuses it if CISO Assistant changed since.");
    }

    /// <summary>The day a saved plan was made, and whether the scope files and config it was made from are still as they were.</summary>
    internal static async Task<(string Day, bool FilesUnchanged)?> SavedStateAsync(string file, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        if (!File.Exists(file) || SavedPlanFile.Read(await File.ReadAllTextAsync(file, cancellationToken), file) is not Result<SavedPlan>.Ok { Value: var saved })
        {
            return null;
        }

        var scopeDir = new DirectoryInfo(saved.Scope);
        var config = saved.Config is null ? null : new FileInfo(saved.Config);
        var checkedScope = await RepoScope.ValidateAsync(scopeDir, config, registry, DateOnly.ParseExact(saved.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
        return (saved.Day, checkedScope.Problems.Count == 0 && await InputsSha256Async(scopeDir, config, checkedScope.Inputs, cancellationToken) == saved.InputsSha256);
    }

    /// <summary>
    /// For the review session: shows the saved plan, refusing it if CISO Assistant changed since it was made, and applies it once
    /// <paramref name="confirm"/> says so. The file is deleted once applied: a plan is applied once.
    /// </summary>
    internal static async Task<int> ApplyPreparedAsync(
        string file, CisoAssistantOptions connection, CollectorRegistry registry, ILoggerFactory loggers, TextWriter output, Func<CancellationToken, Task<bool>> confirm, CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger("hound.scope");
        var shown = await ApplySavedAsync(file, connection, dryRun: true, registry, loggers, output, logger, cancellationToken);
        if (shown != ExitCodes.Ok || !await confirm(cancellationToken))
        {
            return shown;
        }

        var applied = await ApplySavedAsync(file, connection, dryRun: false, registry, loggers, output, logger, cancellationToken);
        if (applied == ExitCodes.Ok)
        {
            File.Delete(file);
        }

        return applied;
    }

    /// <summary>The plan the scope would make against CISO Assistant now, without notes, output or prune: for <c>hound next</c> (s18-01). Reads only.</summary>
    internal static async Task<Result<ScopePlan>> PendingPlanAsync(CheckedScope checkedScope, CisoAssistantOptions connection, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var declared = ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs);
        var actual = await services.GetRequiredService<IScopeStateSource>().ReadAsync(ScopeStateQuery.For(checkedScope.Scope, declared), cancellationToken);
        return actual.Map(held => ScopePlanner.Plan(declared, held, prune: false));
    }

    private static async Task<int> PlanAsync(
        CheckedScope checkedScope, CisoAssistantOptions connection, PlanOptions options, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var declared = ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs);
        var source = services.GetRequiredService<IScopeStateSource>();
        var query = ScopeStateQuery.For(checkedScope.Scope, declared);
        query = query with { Settings = query.Settings || options.ShowUnmanaged };
        var actual = await source.ReadAsync(query, cancellationToken);
        var access = query.Access ? await source.ReadAccessAsync(cancellationToken) : Result.Ok<IReadOnlyList<UserAccess>>([]);
        return await actual.Bind(held => access.Map(users => (Held: held, Users: users))).Match(
            async read =>
            {
                var suppliers = Suppliers(checkedScope, read.Held);
                IReadOnlyList<ScopeProblem> refused = [.. suppliers.Refused, .. LibraryValidation.Refused(declared, read.Held)];
                if (refused.Count > 0)
                {
                    return await ReportAsync(refused, "", options.Format == "json" ? "json" : "text", output, logger);
                }

                var plan = ScopePlanner.Plan(declared, read.Held, options.Prune);
                var notes = new PlanNotes(ScopePeriods.Notes(declared, plan), AccessReview.Notes(checkedScope.Scope.Access, read.Users))
                {
                    Suppliers = suppliers.Notes,
                    Exceptions = ExceptionReview.Notes(read.Held, checkedScope.Inputs.Today ?? DateOnly.FromDateTime(DateTime.UtcNow)),
                    Gaps = CapabilityMap.Gaps(checkedScope.Scope, checkedScope.Inputs.Snapshots),
                    Dependencies = OpenDependencies.Of(checkedScope.Scope, checkedScope.Inputs.Targets ?? []),
                    Unmanaged = options.ShowUnmanaged ? UnmanagedSettings(declared, read.Held) : [],
                    Compliance = ComplianceImpact.Of(plan, declared, read.Held, checkedScope.Inputs),
                };
                await output.WriteAsync(options.Format == "json" ? ScopePlanOutput.Json(plan, notes) : ScopePlanOutput.Markdown(plan, notes));
                if (options.Save is { } save)
                {
                    var saved = new SavedPlan(
                        connection.ApiUrl.ToString(),
                        (checkedScope.Inputs.Today ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        save.Scope.ToString(),
                        save.Config?.ToString(),
                        await InputsSha256Async(save.Scope, save.Config, checkedScope.Inputs, cancellationToken),
                        checkedScope.Scope.Setup.RootFolder,
                        plan,
                        notes.Compliance,
                        Fingerprints(plan, read.Held));
                    await File.WriteAllTextAsync(save.File.FullName, SavedPlanFile.Write(saved), cancellationToken);
                }

                return options.DetailedExitCode && !plan.IsEmpty ? PlanHasChanges : ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>The supplier gate on the day the scope is checked for: systems refused until their suppliers are approved, and notes.</summary>
    private static SupplierReviewResult Suppliers(CheckedScope checkedScope, IReadOnlyList<ActualObject> held) =>
        SupplierReview.Check(checkedScope.Scope, held, checkedScope.Inputs.Today ?? DateOnly.FromDateTime(DateTime.UtcNow));

    /// <summary>The flags and general settings CISO Assistant holds that the scope does not name, by kind and name.</summary>
    private static ScopeObject[] UnmanagedSettings(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> held)
    {
        var named = declared.Select(d => (d.Kind, d.Key)).ToHashSet();
        return [.. held.Select(h => h.Held).Where(h => h.Kind is ScopeKind.FeatureFlag or ScopeKind.Setting && !named.Contains((h.Kind, h.Key)))
            .OrderBy(h => h.Kind).ThenBy(h => h.Key, StringComparer.Ordinal)];
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

    private static async Task<int> ReportAsync(
        IReadOnlyList<ScopeProblem> problems, string summary, string format, TextWriter output, ILogger logger, IReadOnlyList<ScopeProblem>? warnings = null)
    {
        warnings ??= [];
        if (format == "json")
        {
            var json = new JsonArray([.. problems.Select(p => Json(p, "error")), .. warnings.Select(p => Json(p, "warning"))]);
            await output.WriteLineAsync(json.ToJsonString(CommandJson.Escaped));
            return problems.Count == 0 ? ExitCodes.Ok : ExitCodes.Configuration;
        }

        await WarnAsync(warnings, output);
        if (problems.Count == 0)
        {
            await output.WriteLineAsync(summary);
            return ExitCodes.Ok;
        }

        return Log.Problems(logger, problems.Select(p => new Problem(p.Code, $"{Where(p.At)}: {p.Message} [{p.Code}] {p.Hint}")));
    }

    /// <summary>Each warning on its own line: a finding to act on, which blocks nothing and joins the weekly review.</summary>
    internal static async Task WarnAsync(IReadOnlyList<ScopeProblem> warnings, TextWriter output)
    {
        foreach (var w in warnings)
        {
            await output.WriteLineAsync($"warning: {Where(w.At)}: {w.Message} [{w.Code}] {w.Hint}");
        }
    }

    private static JsonObject Json(ScopeProblem p, string severity) => new()
    {
        ["code"] = p.Code,
        ["severity"] = severity,
        ["file"] = p.At.File,
        ["line"] = p.At.Line,
        ["message"] = p.Message,
        ["hint"] = p.Hint,
    };

    private static string Where(SourceLine at) =>
        at.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"{at.File} line {at.Line}") : at.File;
}
