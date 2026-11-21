using EvidenceHound.Core;
using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime;

/// <summary>One collector's stored run: what it reported, and where its evidence went.</summary>
public sealed record CollectedRun(CollectorInstance Instance, CollectorOutput Output, EvidenceManifest Manifest, StoredRun Stored);

/// <summary>Collects from one collector instance and stores its evidence: resolve secrets, collect, build the manifest, write, apply retention.</summary>
public sealed class CollectStep(ISecretProvider secrets, IEvidenceStore store, IClock clock, int? retentionDays)
{
    public Task<Result<CollectedRun>> RunAsync(CollectorInstance instance, ICollector collector, CancellationToken cancellationToken) =>
        throw new NotImplementedException(string.Join(", ", secrets, store, clock, retentionDays));
}
