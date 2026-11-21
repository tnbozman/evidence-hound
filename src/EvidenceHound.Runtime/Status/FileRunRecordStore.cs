using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Status;

/// <summary>Keeps the latest run record per collector.</summary>
public interface IRunRecordStore
{
    Task SaveAsync(RunRecord record, CancellationToken cancellationToken);

    /// <summary>The latest record of every collector that has run, by collector name.</summary>
    Task<Result<IReadOnlyList<RunRecord>>> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>Stores each collector's latest run record as <c>&lt;root&gt;/_status/&lt;collector&gt;.json</c>.</summary>
public sealed class FileRunRecordStore(string root) : IRunRecordStore
{
    public Task SaveAsync(RunRecord record, CancellationToken cancellationToken) => throw new NotImplementedException(root);

    public Task<Result<IReadOnlyList<RunRecord>>> LatestAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}
