using System.Globalization;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// Saved scope plans: <c>hound scope apply &lt;plan&gt;</c> applies one only while the scope files and what CISO Assistant holds are what
/// the plan read, and <c>hound review prepare</c> saves the plan the person's session applies.
/// </summary>
internal static partial class ScopeCommands
{
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
}
