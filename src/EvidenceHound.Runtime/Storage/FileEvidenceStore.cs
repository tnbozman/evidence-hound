using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Storage;

/// <summary>Stores runs as <c>&lt;root&gt;/&lt;collector&gt;/&lt;UTC timestamp&gt;/</c> with the files and <c>_manifest.json</c>.</summary>
public sealed class FileEvidenceStore(string root) : IEvidenceStore
{
    public string Root { get; } = root;

    public Task<Result<StoredRun>> WriteRunAsync(EvidenceManifest manifest, IReadOnlyList<CollectedFile> files, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<StoredRun>> ListRunsAsync(string collector, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task DeleteRunAsync(StoredRun run, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
