using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Status;

/// <summary>One run's effect on one audit: when, which run, and how many rows it changed, verified unchanged and restored from drift.</summary>
public sealed record HistoryEntry(DateTimeOffset Date, string RunId, int Changed, int Unchanged, int Drift);

/// <summary>An append-only history of runs per audit.</summary>
public interface IRunHistory
{
    Task AppendAsync(string auditRefId, HistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>The latest entry of every audit that has one, by audit ref_id.</summary>
    Task<Result<IReadOnlyList<(string Audit, HistoryEntry Entry)>>> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>Appends one JSON line per run to <c>&lt;root&gt;/_history/&lt;audit&gt;.jsonl</c>.</summary>
public sealed class FileRunHistory(string root) : IRunHistory
{
    public Task AppendAsync(string auditRefId, HistoryEntry entry, CancellationToken cancellationToken) => throw new NotImplementedException(root);

    public Task<Result<IReadOnlyList<(string Audit, HistoryEntry Entry)>>> LatestAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}
