using EvidenceHound.Collectors;
using EvidenceHound.Core;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Pipeline;
using EvidenceHound.Runtime.Status;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime;

/// <summary>What <c>hound run</c> does. <paramref name="DryRun"/> plans without sending; <paramref name="NoPublish"/> stops after storing evidence.</summary>
public sealed record RunOptions(string? Only = null, bool DryRun = false, bool NoPublish = false);

public enum CollectorOutcome
{
    Succeeded,
    Failed,
}

/// <summary>One collector instance's result: its outcome, the problems behind a failure, and the publish report if it got that far.</summary>
public sealed record CollectorRunResult(string Collector, CollectorOutcome Outcome, IReadOnlyList<Problem> Problems, PublishReport? Publish)
{
    /// <summary>The run record saved for this run.</summary>
    public RunRecord? Record { get; init; }

    /// <summary>The report of publishing the health evidence, when the run published.</summary>
    public PublishReport? Health { get; init; }

    /// <summary>Rows changed, verified unchanged and restored from drift, per target audit, when the run planned.</summary>
    public IReadOnlyList<AuditChanges> Changes { get; init; } = [];

    /// <summary>A <c>publish.drift_corrected</c> notice for each computed row someone edited in CISO Assistant, which this run restores.</summary>
    public IReadOnlyList<Problem> Drift { get; init; } = [];

    public bool Equals(CollectorRunResult? other) =>
        other is not null && Collector == other.Collector && Outcome == other.Outcome && Problems.SequenceEqual(other.Problems) && Equals(Publish, other.Publish);

    public override int GetHashCode() => HashCode.Combine(Collector, Outcome, Problems.Count, Publish);
}

/// <summary>Every collector's result, in config order.</summary>
public sealed record RunSummary(IReadOnlyList<CollectorRunResult> Collectors)
{
    /// <summary>The run's ID, from its start time, as written into each changed row's observation.</summary>
    public string RunId { get; init; } = "";

    /// <summary>Each audit's changes summed over the collectors.</summary>
    public IReadOnlyList<AuditChanges> Audits =>
        [.. Collectors.SelectMany(c => c.Changes).GroupBy(a => a.ComplianceAssessmentRefId, StringComparer.Ordinal)
            .Select(g => new AuditChanges(g.Key, g.Sum(a => a.Changed), g.Sum(a => a.Unchanged), g.Sum(a => a.Drift)))];

    /// <summary>The worst outcome: failed if any collector failed.</summary>
    public CollectorOutcome Outcome => Collectors.Any(c => c.Outcome == CollectorOutcome.Failed) ? CollectorOutcome.Failed : CollectorOutcome.Succeeded;
}

/// <summary>One audit a run publishes to: its mapping, where results go, and the applied control its health evidence is linked to.</summary>
public sealed record RunTarget(FrameworkMapping Mapping, AuditTarget Target, string? HealthControlRefId = null);

/// <summary>
/// Runs collect, store, evaluate, map, plan and publish for each collector instance. One collector failing, even by throwing,
/// does not stop the others.
/// </summary>
public sealed class Runner(
    IReadOnlyDictionary<string, ICollector> collectors,
    CollectStep collect,
    IReadOnlyList<RunTarget> targets,
    Func<IEvidenceFiles, IAuditUpdateSink> sinkFor,
    IClock clock,
    IRunRecordStore records)
{
    /// <summary>Where the current state comes from; by default nothing is known and everything is sent.</summary>
    public ICurrentStateSource States { get; init; } = NoCurrentState.Instance;

    /// <summary>Where each published run's per-audit counts are appended; by default nowhere.</summary>
    public IRunHistory? History { get; init; }

    /// <summary>
    /// s14-05: where each collector's latest results are saved and read, so a row whose checks come from several collectors is filled by
    /// whichever of them runs; by default nothing is saved and each collector fills only the rows it fully reports.
    /// </summary>
    public ILatestResults Latest { get; init; } = NoLatestResults.Instance;

    /// <summary>A runner that publishes to one audit.</summary>
    public Runner(
        IReadOnlyDictionary<string, ICollector> collectors,
        CollectStep collect,
        FrameworkMapping mapping,
        AuditTarget target,
        Func<IEvidenceFiles, IAuditUpdateSink> sinkFor,
        IClock clock,
        IRunRecordStore records,
        string? healthControlRefId = null)
        : this(collectors, collect, [new RunTarget(mapping, target, healthControlRefId)], sinkFor, clock, records)
    {
    }

    /// <summary>The instances to run: all of them, or the one named by <paramref name="only"/> (<c>run.collector_unknown</c> if none has that name).</summary>
    public static Result<IReadOnlyList<CollectorInstance>> Select(IReadOnlyList<CollectorInstance> instances, string? only)
    {
        ArgumentNullException.ThrowIfNull(instances);
        return only is null ? Result.Ok(instances)
            : instances.Where(i => i.Name == only).ToArray() is [_, ..] found ? Result.Ok<IReadOnlyList<CollectorInstance>>(found)
            : Result.Fail<IReadOnlyList<CollectorInstance>>(new Problem(
                "run.collector_unknown", $"No collector is named '{only}'; configured: {string.Join(", ", instances.Select(i => i.Name))}."));
    }

    public async Task<RunSummary> RunAsync(IReadOnlyList<CollectorInstance> instances, RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(options);

        var started = clock.UtcNow;
        var runId = RunIds.From(started);
        var results = new List<CollectorRunResult>(instances.Count);
        foreach (var instance in instances)
        {
            results.Add(await RunOneAsync(instance, options, runId, cancellationToken));
        }

        var summary = new RunSummary(results) { RunId = runId };
        if (History is { } history && !options.DryRun && !options.NoPublish)
        {
            foreach (var audit in summary.Audits)
            {
                await history.AppendAsync(audit.ComplianceAssessmentRefId, new HistoryEntry(started, runId, audit.Changed, audit.Unchanged, audit.Drift), cancellationToken);
            }
        }

        return summary;
    }

    private async Task<CollectorRunResult> RunOneAsync(CollectorInstance instance, RunOptions options, string runId, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        var collected = await CollectAsync(instance, cancellationToken);
        var result = options.NoPublish
            ? collected.Match(_ => Succeeded(instance, null), problems => Failed(instance, problems, null))
            : await PublishAsync(instance, collected, options, runId, cancellationToken);

        if (collected is Result<CollectedRun>.Ok { Value: var run } && RunEvidence.From(run) is Result<CollectorResults>.Ok { Value: var saved })
        {
            await Latest.SaveAsync(new LatestResults(instance.Name, saved, run.Stored.Location), cancellationToken);
        }

        var checks = collected.Match(run => run.Output.Checks, _ => []);
        var record = RunRecords.Summarise(instance.Name, started, clock.UtcNow, result.Outcome, checks, result.Problems, result.Publish);
        await records.SaveAsync(record, cancellationToken);
        if (options.NoPublish)
        {
            return result with { Record = record };
        }

        var health = Health.Plan(record, [.. targets.Select(t => t.HealthControlRefId).OfType<string>().Distinct(StringComparer.Ordinal)]);
        var healthReport = options.DryRun ? Publisher.DryRun([health.Upload]) : await SendAsync(RunEvidence.Files([health.File]), [health.Upload], cancellationToken);
        var healthProblems = FailedItems(healthReport);
        return (healthProblems.Count > 0 ? Failed(instance, [.. result.Problems, .. healthProblems], result.Publish) : result) with { Record = record, Health = healthReport };
    }

    private async Task<Result<CollectedRun>> CollectAsync(CollectorInstance instance, CancellationToken cancellationToken)
    {
        try
        {
            return await collect.RunAsync(instance, collectors[instance.Type], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<CollectedRun>(new Problem("collector.crashed", $"The {instance.Type} collector failed unexpectedly: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    private async Task<CollectorRunResult> PublishAsync(
        CollectorInstance instance, Result<CollectedRun> collected, RunOptions options, string runId, CancellationToken cancellationToken)
    {
        var others = await Latest.OthersAsync(instance.Name, cancellationToken);
        var otherChecks = others.SelectMany(o => o.Results.Checks).ToArray();
        var inputs = collected.Bind(run => RunEvidence.From(run).Map(mine =>
        {
            IReadOnlyList<TargetResults> owned = [.. targets.Select(t => new TargetResults(t.Target, Ownership.OwnedResults(t.Mapping, mine.Checks, otherChecks)))];
            var cited = owned.SelectMany(t => t.Results).SelectMany(r => r.EvidenceRefs).ToHashSet(StringComparer.Ordinal);
            var borrowed = others.SelectMany(o => o.Results.Evidence).Where(e => cited.Contains(e.Id) && mine.Evidence.All(m => m.Id != e.Id)).DistinctBy(e => e.Id);
            return (Run: run, Results: mine with { Evidence = [.. mine.Evidence, .. borrowed] }, Targets: owned);
        }));
        var planned = await inputs.BindAsync(async i =>
        {
            var current = options.DryRun
                ? CurrentState.Empty
                : await States.ReadAsync(StateQuery.For(i.Targets, i.Results.Evidence, i.Results.ControlStatuses), cancellationToken);
            return AuditPlanner.PlanChanges(i.Targets, i.Results.Evidence, i.Results.ControlStatuses, current, runId).Map(plan => (i.Run, Plan: plan));
        });

        return await planned.Match(
            async p =>
            {
                var report = options.DryRun ? Publisher.DryRun(p.Plan.Updates) : await SendAsync(LatestFiles.Over(RunEvidence.Files(p.Run), others), p.Plan.Updates, cancellationToken);
                var problems = FailedItems(report);
                return (problems.Count > 0 ? Failed(instance, problems, report) : Succeeded(instance, report)) with { Changes = p.Plan.Audits, Drift = p.Plan.Drift };
            },
            problems => Task.FromResult(Failed(instance, problems, null)));
    }

    private async Task<PublishReport> SendAsync(IEvidenceFiles files, IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken)
    {
        var sink = sinkFor(files);
        try
        {
            return await sink.SendAsync(updates, cancellationToken);
        }
        finally
        {
            (sink as IDisposable)?.Dispose();
        }
    }

    private static IReadOnlyList<Problem> FailedItems(PublishReport report) =>
        [.. report.Items.Where(i => i.Outcome == PublishOutcome.Failed).SelectMany(i => i.Problems.Select(p => new Problem(p.Code, $"{i.Description}: {p.Message}")))];

    private static CollectorRunResult Succeeded(CollectorInstance instance, PublishReport? report) => new(instance.Name, CollectorOutcome.Succeeded, [], report);

    private static CollectorRunResult Failed(CollectorInstance instance, IReadOnlyList<Problem> problems, PublishReport? report) =>
        new(instance.Name, CollectorOutcome.Failed, problems, report);
}
