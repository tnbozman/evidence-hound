using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.FileSystemGlobbing;

namespace EvidenceHound.Cli;

/// <summary>A checked scope: its <see cref="Problems"/> block plan and apply; its <see cref="Warnings"/> are reported and block nothing.</summary>
internal sealed record CheckedScope(IReadOnlyList<ScopeProblem> Problems, string Summary, ScopeDocument Scope, ScopeInputs Inputs)
{
    public IReadOnlyList<ScopeProblem> Warnings { get; init; } = [];
}

/// <summary>
/// Reading and checking a compliance repo's scope folder offline, with the deployment config it names: what every command that works on
/// the scope starts from.
/// </summary>
internal static class RepoScope
{
    /// <summary>The reporting obligations in <c>reporting.yaml</c> (s15-07); none when the scope has no such file.</summary>
    internal static async Task<(IReadOnlyList<ReportingObligation> Obligations, IReadOnlyList<ScopeProblem> Problems)> ReportingAsync(
        DirectoryInfo dir, CancellationToken cancellationToken, Func<string, CancellationToken, Task<ScopeSource>>? read = null)
    {
        var path = Path.Combine(dir.ToString(), "reporting.yaml");
        return File.Exists(path) ? ScopeLoader.ParseReporting(await (read ?? ReadFileAsync)(path, cancellationToken)) : ([], []);
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

        var (irp, problems) = ScopeLoader.ParseIrp(await (read ?? ReadFileAsync)(path, cancellationToken));
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
            edits is not null && edits.TryGetValue(Path.GetFullPath(path), out var text) ? Task.FromResult(new ScopeSource(path, text)) : ReadFileAsync(path, ct);

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
            string.Create(CultureInfo.InvariantCulture, $"scope valid: {scope.Setup.Domains.Count} domain(s), {scope.Controls.Count} control(s), {scope.Audits.Count} audit(s){ThirdPartyCount(scope)}{RiskCount(scope)}{MetricCount(scope)}"),
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
    private static string MetricCount(ScopeDocument scope) =>
        scope.Metrics.Instances.Count + scope.Metrics.Dashboards.Count == 0
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $", {scope.Metrics.Instances.Count} metric(s) with {scope.Metrics.Dashboards.Count} dashboard(s)");

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

    internal static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
    }

    internal static async Task<ScopeSource> ReadFileAsync(string path, CancellationToken cancellationToken) =>
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

    /// <summary>What scope validation needs from the deployment config: its targets, and its collector instances by name.</summary>
    private sealed record Deployment(IReadOnlyList<ScopeTarget> Targets, IReadOnlyList<string> Collectors)
    {
        public static Deployment None { get; } = new([], []);
    }

    private static async Task<Result<Deployment>> DeploymentAsync(FileInfo configFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var config = await DeploymentFile.LoadAsync(configFile, registry, cancellationToken);
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
}
