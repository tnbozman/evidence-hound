using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime;

/// <summary>What <c>hound run</c> does. <paramref name="DryRun"/> plans without sending; <paramref name="NoPublish"/> stops after storing evidence.</summary>
public sealed record RunOptions(string? Only = null, bool DryRun = false, bool NoPublish = false);

public enum CollectorOutcome
{
    Succeeded,
    Failed,
}

/// <summary>One collector instance's result: its outcome, the problems behind a failure, and the publish report if it got that far.</summary>
public sealed record CollectorRunResult(string Collector, CollectorOutcome Outcome, IReadOnlyList<Problem> Problems, PublishReport? Publish);

/// <summary>Every collector's result, in config order.</summary>
public sealed record RunSummary(IReadOnlyList<CollectorRunResult> Collectors)
{
    /// <summary>The worst outcome: failed if any collector failed.</summary>
    public CollectorOutcome Outcome => throw new NotImplementedException();
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
    Func<IEvidenceFiles, IAuditUpdateSink> sinkFor)
{
    /// <summary>The instances to run: all of them, or the one named by <paramref name="only"/> (<c>run.collector_unknown</c> if none has that name).</summary>
    public static Result<IReadOnlyList<CollectorInstance>> Select(IReadOnlyList<CollectorInstance> instances, string? only) =>
        throw new NotImplementedException();

    public Task<RunSummary> RunAsync(IReadOnlyList<CollectorInstance> instances, RunOptions options, CancellationToken cancellationToken) =>
        throw new NotImplementedException(string.Join(", ", collectors, collect, mapping, target, sinkFor));
}
