using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Packaging;

/// <summary>One stored run of one collector. <paramref name="Complete"/> is true when the run's manifest was written.</summary>
public sealed record StoredRun(string Collector, DateTimeOffset StartedAt, string Location, bool Complete);

/// <summary>Keeps each collector run's files and manifest, one folder per run.</summary>
public interface IEvidenceStore
{
    /// <summary>Writes the files, then the manifest, into a new run for the manifest's collector and time.</summary>
    Task<Result<StoredRun>> WriteRunAsync(EvidenceManifest manifest, IReadOnlyList<CollectedFile> files, CancellationToken cancellationToken);

    /// <summary>The collector's runs, oldest first.</summary>
    Task<IReadOnlyList<StoredRun>> ListRunsAsync(string collector, CancellationToken cancellationToken);

    Task DeleteRunAsync(StoredRun run, CancellationToken cancellationToken);
}
