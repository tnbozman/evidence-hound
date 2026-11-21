using EvidenceHound.Collectors;
using EvidenceHound.Core;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Pipeline;
using EvidenceHound.Runtime.Status;

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

    public bool Equals(CollectorRunResult? other) =>
        other is not null && Collector == other.Collector && Outcome == other.Outcome && Problems.SequenceEqual(other.Problems) && Equals(Publish, other.Publish);

    public override int GetHashCode() => HashCode.Combine(Collector, Outcome, Problems.Count, Publish);
}

/// <summary>Every collector's result, in config order.</summary>
public sealed record RunSummary(IReadOnlyList<CollectorRunResult> Collectors)
{
    /// <summary>The worst outcome: failed if any collector failed.</summary>
    public CollectorOutcome Outcome => Collectors.Any(c => c.Outcome == CollectorOutcome.Failed) ? CollectorOutcome.Failed : CollectorOutcome.Succeeded;
}

/// <summary>
/// Runs collect, store, evaluate, map, plan and publish for each collector instance. One collector failing, even by throwing,
/// does not stop the others.
/// </summary>
public sealed class Runner(
    IReadOnlyDictionary<string, ICollector> collectors,
    CollectStep collect,
    FrameworkMapping mapping,
    AuditTarget target,
    Func<IEvidenceFiles, IAuditUpdateSink> sinkFor,
    IClock clock,
    IRunRecordStore records,
    string? healthControlRefId = null)
{
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

        var results = new List<CollectorRunResult>(instances.Count);
        foreach (var instance in instances)
        {
            results.Add(await RunOneAsync(instance, options, cancellationToken));
        }

        return new RunSummary(results);
    }

    private async Task<CollectorRunResult> RunOneAsync(CollectorInstance instance, RunOptions options, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        var collected = await CollectAsync(instance, cancellationToken);
        var result = options.NoPublish
            ? collected.Match(_ => Succeeded(instance, null), problems => Failed(instance, problems, null))
            : await PublishAsync(instance, collected, options, cancellationToken);

        var checks = collected.Match(run => run.Output.Checks, _ => []);
        var record = RunRecords.Summarise(instance.Name, started, clock.UtcNow, result.Outcome, checks, result.Problems, result.Publish);
        await records.SaveAsync(record, cancellationToken);
        if (options.NoPublish)
        {
            return result with { Record = record };
        }

        var health = Health.Plan(record, healthControlRefId);
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

    private async Task<CollectorRunResult> PublishAsync(CollectorInstance instance, Result<CollectedRun> collected, RunOptions options, CancellationToken cancellationToken)
    {
        var planned = collected.Bind(run => RunEvidence.From(run).Bind(results => AuditPlanner.PlanUpdates(
            target, Ownership.OwnedResults(mapping, results.Checks), results.Evidence, results.ControlStatuses).Map(updates => (Run: run, Updates: updates))));

        return await planned.Match(
            async plan =>
            {
                var report = options.DryRun ? Publisher.DryRun(plan.Updates) : await SendAsync(RunEvidence.Files(plan.Run), plan.Updates, cancellationToken);
                var problems = FailedItems(report);
                return problems.Count > 0 ? Failed(instance, problems, report) : Succeeded(instance, report);
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
