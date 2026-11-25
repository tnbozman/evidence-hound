using System.CommandLine;
using System.Globalization;
using System.Text.Json;
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
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound scope</c> commands: check a compliance repo's scope files, start a new audit file, and plan the scope against CISO Assistant.</summary>
internal static class ScopeCommands
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
            var checkedScope = await ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
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
            $"Show what applying the scope would create, update or delete in CISO Assistant, and what drifted (reads it; with --config, uses the config's ciso_assistant address and token unless {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting} override them; without it, needs both). Sends nothing.")
        {
            dir, config, planFormat, planJson, prune, showUnmanaged, detailedExitCode, planOut,
        };
        plan.SetAction(async (parseResult, cancellationToken) =>
        {
            var detailed = parseResult.GetValue(detailedExitCode);
            var checkedScope = await ValidateAsync(parseResult.GetRequiredValue(dir), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            if (!parseResult.GetValue(planJson) && parseResult.GetRequiredValue(planFormat) != "json")
            {
                await WarnAsync(checkedScope.Warnings, output);
            }

            var exitCode = checkedScope.Problems.Count > 0
                ? await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger)
                : await (await PublishCommand.ConnectionAsync(configuration, parseResult.GetValue(config), registry, cancellationToken)).Match(
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
            $"Write the scope to CISO Assistant, in dependency order: libraries, domains, perimeters, systems, audits, documents, controls and rows (with --config, uses the config's ciso_assistant address and token unless {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting} override them; without it, needs both). Safe to run again. Give --config so the unlisted rule leaves the rows a mapping computes alone. Given a saved plan, applies it without planning again, and refuses it if the scope or CISO Assistant changed since.")
        {
            scopeOrPlan, config, dryRun, prune,
        };
        apply.SetAction(async (parseResult, cancellationToken) =>
        {
            var target = parseResult.GetRequiredValue(scopeOrPlan);
            if (File.Exists(target))
            {
                return await PublishCommand.Connection(configuration).Match(
                    connection => ApplySavedAsync(target, connection, parseResult.GetValue(dryRun), registry, loggers, output, logger, cancellationToken),
                    problems => Task.FromResult(Log.Problems(logger, problems)));
            }

            var checkedScope = await ValidateAsync(new DirectoryInfo(target), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            await WarnAsync(checkedScope.Warnings, output);
            if (checkedScope.Problems.Count > 0)
            {
                return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
            }

            return await (await PublishCommand.ConnectionAsync(configuration, parseResult.GetValue(config), registry, cancellationToken)).Match(
                connection => ApplyAsync(checkedScope, connection, parseResult.GetValue(prune), parseResult.GetValue(dryRun), loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        var outDir = new Option<DirectoryInfo>("--out") { Description = "The scope folder to write; documents go next to it, in the compliance repo.", Required = true };
        var rootFolder = new Option<string>("--root-folder") { Description = "The folder the domains sit under (default Global).", DefaultValueFactory = _ => "Global" };
        var withFiles = new Option<bool>("--with-files") { Description = "Also download each document's file into documents/<id>/ in the compliance repo." };
        var export = new Command(
            "export",
            $"Write what CISO Assistant holds under the root folder as scope files: setup, controls, one file per audit and framework snapshots (reads it; needs {PublishCommand.UrlSetting} and {PublishCommand.TokenSetting}). Results runs compute and their evidence are left out.")
        {
            outDir, rootFolder, withFiles,
        };
        export.SetAction((parseResult, cancellationToken) => PublishCommand.Connection(configuration).Match(
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
        var checkedScope = await ValidateAsync(scope, config, registry, AuditSeries.Today(configuration), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return await ReportAsync(checkedScope.Problems, checkedScope.Summary, "text", output, logger);
        }

        return await (await PublishCommand.ConnectionAsync(configuration, config, registry, cancellationToken)).Match(
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
        var checkedScope = await ValidateAsync(scopeDir, config, registry, DateOnly.ParseExact(saved.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
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
            lines.Add($"scope/{path.Relative} {await Sha256Async(path.Full, cancellationToken)}");
        }

        if (config is not null && config.Exists)
        {
            lines.Add($"config {await Sha256Async(config.FullName, cancellationToken)}");
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

    /// <summary>A checked scope: its problems, a one-line summary, the parsed files, and what planning it needs from outside them.</summary>
    /// <summary>A checked scope: its <see cref="Problems"/> block plan and apply; its <see cref="Warnings"/> are reported and block nothing.</summary>
    internal sealed record CheckedScope(IReadOnlyList<ScopeProblem> Problems, string Summary, ScopeDocument Scope, ScopeInputs Inputs)
    {
        public IReadOnlyList<ScopeProblem> Warnings { get; init; } = [];
    }

    /// <summary>The reporting obligations in <c>reporting.yaml</c> (s15-07); none when the scope has no such file.</summary>
    internal static async Task<(IReadOnlyList<ReportingObligation> Obligations, IReadOnlyList<ScopeProblem> Problems)> ReportingAsync(
        DirectoryInfo dir, CancellationToken cancellationToken, Func<string, CancellationToken, Task<ScopeSource>>? read = null)
    {
        var path = Path.Combine(dir.ToString(), "reporting.yaml");
        return File.Exists(path) ? ScopeLoader.ParseReporting(await (read ?? ReadAsync)(path, cancellationToken)) : ([], []);
    }

    /// <summary>The incident response plan in <c>irp.yaml</c> (s15-09), with its problems; null when the scope has no such file.</summary>
    internal static async Task<(ScopeIrp? Irp, IReadOnlyList<ScopeProblem> Problems)> IrpAsync(
        DirectoryInfo dir, ScopeDocument scope, CancellationToken cancellationToken, Func<string, CancellationToken, Task<ScopeSource>>? read = null)
    {
        var path = Path.Combine(dir.ToString(), "irp.yaml");
        if (!File.Exists(path))
        {
            return (null, []);
        }

        var (irp, problems) = ScopeLoader.ParseIrp(await (read ?? ReadAsync)(path, cancellationToken));
        return irp is not null && scope.Setup.Programmes.All(p => p.Id != irp.Programme.Value)
            ? (irp, [.. problems, new ScopeProblem("irp.programme_unknown", irp.Programme.At, $"programme '{irp.Programme.Value}' is not declared.", "Name a programme from 'programmes:' in scope/setup.yaml.")])
            : (irp, problems);
    }

    /// <summary>
    /// Checks the scope folder offline. <paramref name="edits"/> stands in for files by full path, so a wizard checks its change before
    /// it writes anything.
    /// </summary>
    internal static async Task<CheckedScope> ValidateAsync(
        DirectoryInfo dir, FileInfo? configFile, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? edits = null)
    {
        Task<ScopeSource> ReadAsync(string path, CancellationToken ct) =>
            edits is not null && edits.TryGetValue(Path.GetFullPath(path), out var text) ? Task.FromResult(new ScopeSource(path, text)) : ScopeCommands.ReadAsync(path, ct);

        var setupPath = Path.Combine(dir.ToString(), "setup.yaml");
        if (!File.Exists(setupPath))
        {
            return new([new ScopeProblem("scope.file_missing", new SourceLine(setupPath, 0), "the scope has no setup.yaml.", "Create setup.yaml with the organisation, frameworks and domains.")], "", new ScopeDocument(new ScopeSetup("Global", [], []), [], []), ScopeInputs.None);
        }

        var controlsPath = Path.Combine(dir.ToString(), "controls.yaml");
        var auditsDir = Path.Combine(dir.ToString(), "audits");
        var audits = Directory.Exists(auditsDir) ? Directory.GetFiles(auditsDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var accessPath = Path.Combine(dir.ToString(), "access.yaml");
        var settingsPath = Path.Combine(dir.ToString(), "settings.yaml");
        var thirdPartiesDir = Path.Combine(dir.ToString(), "third-parties");
        var thirdParties = Directory.Exists(thirdPartiesDir) ? Directory.GetFiles(thirdPartiesDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var capabilitiesPath = Path.Combine(dir.ToString(), "capabilities.yaml");
        var planPath = Path.Combine(dir.ToString(), "evidence-plan.yaml");
        var sspPath = Path.Combine(dir.ToString(), "ssp.yaml");
        var risksDir = Path.Combine(dir.ToString(), "risks");
        var risks = Directory.Exists(risksDir) ? Directory.GetFiles(risksDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var exceptionsPath = Path.Combine(dir.ToString(), "exceptions.yaml");
        var biaDir = Path.Combine(dir.ToString(), "bia");
        var bias = Directory.Exists(biaDir) ? Directory.GetFiles(biaDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var metricsPath = Path.Combine(dir.ToString(), "metrics.yaml");
        var parsed = ScopeLoader.Parse(
            await ReadAsync(setupPath, cancellationToken),
            File.Exists(controlsPath) ? await ReadAsync(controlsPath, cancellationToken) : null,
            [.. await Task.WhenAll(audits.Select(a => ReadAsync(a, cancellationToken)))],
            File.Exists(accessPath) ? await ReadAsync(accessPath, cancellationToken) : null,
            File.Exists(settingsPath) ? await ReadAsync(settingsPath, cancellationToken) : null,
            [.. await Task.WhenAll(thirdParties.Select(t => ReadAsync(t, cancellationToken)))],
            File.Exists(capabilitiesPath) ? await ReadAsync(capabilitiesPath, cancellationToken) : null,
            File.Exists(planPath) ? await ReadAsync(planPath, cancellationToken) : null,
            File.Exists(sspPath) ? await ReadAsync(sspPath, cancellationToken) : null,
            [.. await Task.WhenAll(risks.Select(r => ReadAsync(r, cancellationToken)))],
            File.Exists(exceptionsPath) ? await ReadAsync(exceptionsPath, cancellationToken) : null,
            [.. await Task.WhenAll(bias.Select(b => ReadAsync(b, cancellationToken)))],
            File.Exists(metricsPath) ? await ReadAsync(metricsPath, cancellationToken) : null);

        var snapshots = await SnapshotsAsync(parsed.Scope.Setup.Frameworks, Path.GetDirectoryName(Path.GetFullPath(setupPath))!, cancellationToken);
        var deployment = configFile is null ? Result.Ok(Deployment.None) : await DeploymentAsync(configFile, registry, cancellationToken);
        var targets = deployment.Map(d => d.Targets);
        var scope = parsed.Scope;
        var repoDir = Path.GetDirectoryName(Path.GetFullPath(dir.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))!;
        var files = await FilesAsync(scope, repoDir, Path.GetDirectoryName(Path.GetFullPath(setupPath))!, cancellationToken);
        var libraries = await ScopeLibraries.LoadAsync(scope.Setup.LibraryFiles, Path.GetDirectoryName(Path.GetFullPath(setupPath))!, cancellationToken);
        IReadOnlyList<ScopeProblem> problems =
        [
            .. parsed.Problems,
            .. snapshots.Problems,
            .. libraries.Problems,
            .. files.Problems,
            .. targets.Match(_ => [], ps => ps.Select(p => new ScopeProblem(p.Code, new SourceLine(configFile!.ToString(), 0), p.Message, "Fix the deployment config first."))),
            .. ScopeValidator.Validate(scope, snapshots.Loaded, targets.Match(t => t, _ => [])),
            .. SupplierValidator.Validate(scope, today),
            .. RiskValidator.Validate(scope, libraries.Snapshots),
            .. BiaValidator.Validate(scope, libraries.Snapshots),
            .. MetricValidator.Validate(scope),
            .. (await ReportingAsync(dir, cancellationToken, ReadAsync)).Problems,
            .. (await IrpAsync(dir, scope, cancellationToken, ReadAsync)).Problems,
            .. RunbooksMissing(scope, repoDir),
            .. configFile is null ? [] : deployment.Match(d => EvidencePlanValidator.Collectors(scope, d.Collectors), _ => []),
        ];
        return new(
            [.. problems.Where(p => !p.Warning)],
            string.Create(CultureInfo.InvariantCulture, $"scope valid: {scope.Setup.Domains.Count} domain(s), {scope.Controls.Count} control(s), {scope.Audits.Count} audit(s){ThirdPartyCount(scope)}{RiskCount(scope)}"),
            scope,
            new ScopeInputs(snapshots.Loaded, configFile is null ? null : targets.Match(t => t, _ => []), files.Found.Concat(libraries.Files).ToDictionary(StringComparer.Ordinal))
            {
                Today = today,
                Runbooks = await RunbooksAsync(scope, repoDir, cancellationToken),
                Libraries = libraries.Snapshots,
            })
        {
            Warnings = [.. problems.Where(p => p.Warning)],
        };
    }

    private static string RiskCount(ScopeDocument scope) =>
        scope.RiskStudies.Count == 0
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $", {scope.RiskStudies.Count} risk study(ies) with {scope.RiskStudies.Sum(r => r.Scenarios.Count)} scenario(s)");

    /// <summary>Each runbook the evidence plan names that is in the repo, by its path as the plan names it: the text of its task.</summary>
    private static async Task<Dictionary<string, string>> RunbooksAsync(ScopeDocument scope, string repoDir, CancellationToken cancellationToken)
    {
        var runbooks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in (scope.EvidencePlan?.Rows ?? []).Select(r => r.Runbook).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(path, repoDir);
            if (File.Exists(full))
            {
                runbooks[path] = await File.ReadAllTextAsync(full, cancellationToken);
            }
        }

        return runbooks;
    }

    /// <summary>Each evidence plan runbook must be a file in the compliance repo (the folder that holds the scope folder).</summary>
    private static IEnumerable<ScopeProblem> RunbooksMissing(ScopeDocument scope, string repoDir) =>
        (scope.EvidencePlan?.Rows ?? [])
            .Where(r => r.Runbook is { } runbook && !File.Exists(Path.GetFullPath(runbook, repoDir)))
            .Select(r => new ScopeProblem(
                "evidence_plan.runbook_file_missing",
                r.At,
                $"evidence row '{r.RefId}' names the runbook {r.Runbook}, which is not in the compliance repo.",
                "Add the runbook (what to export, from where, and how to send it to the GRC team), or fix the path: it is relative to the folder that holds scope/."));

    /// <summary>", N third party(ies)" when the scope declares any, so a scope without them reads as before.</summary>
    private static string ThirdPartyCount(ScopeDocument scope) =>
        scope.ThirdParties.Count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $", {scope.ThirdParties.Count} third party(ies)");

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
    /// <summary>What scope validation needs from the deployment config: its targets, and its collector instances by name.</summary>
    private sealed record Deployment(IReadOnlyList<ScopeTarget> Targets, IReadOnlyList<string> Collectors)
    {
        public static Deployment None { get; } = new([], []);
    }

    private static async Task<Result<Deployment>> DeploymentAsync(FileInfo configFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var config = await ConfigCommands.LoadAsync(configFile, registry, cancellationToken);
        return await config.Match(
            async c => (await PlanCommand.LoadMappingsAsync(c.Targets, cancellationToken)).Map(loaded => new Deployment(
            [
                .. loaded.Select(t => new ScopeTarget(
                    t.Target.ComplianceAssessmentRefId,
                    ReportedChecks.Computed(t.Mapping, c, registry.Descriptions),
                    [.. t.Target.Controls.Values.Append(t.Target.HealthControlRefId).OfType<string>().Distinct(StringComparer.Ordinal)],
                    new SourceLine(configFile.ToString(), 0))),
            ],
            [.. c.Collectors.Select(i => i.Name)])),
            problems => Task.FromResult(Result.Fail<Deployment>(problems)));
    }

    private static async Task<int> ReportAsync(
        IReadOnlyList<ScopeProblem> problems, string summary, string format, TextWriter output, ILogger logger, IReadOnlyList<ScopeProblem>? warnings = null)
    {
        warnings ??= [];
        if (format == "json")
        {
            var json = new JsonArray([.. problems.Select(p => Json(p, "error")), .. warnings.Select(p => Json(p, "warning"))]);
            await output.WriteLineAsync(json.ToJsonString(Indented));
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
