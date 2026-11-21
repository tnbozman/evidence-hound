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
    public Task<Result<CollectedRun>> RunAsync(CollectorInstance instance, ICollector collector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(collector);

        var now = clock.UtcNow;
        return Resolve(instance)
            .BindAsync(resolved => collector.CollectAsync(new CollectorContext(instance.Name, instance.Settings, resolved, now), cancellationToken))
            .BindAsync(output => Manifests.Build(instance.Name, collector.Version, now, output.Files)
                .BindAsync(manifest => StoreAsync(instance, output, manifest, cancellationToken)));
    }

    private async Task<Result<CollectedRun>> StoreAsync(CollectorInstance instance, CollectorOutput output, EvidenceManifest manifest, CancellationToken cancellationToken)
    {
        var stored = await store.WriteRunAsync(manifest, output.Files, cancellationToken);
        if (retentionDays is { } days && stored is Result<StoredRun>.Ok)
        {
            var runs = await store.ListRunsAsync(instance.Name, cancellationToken);
            foreach (var run in Retention.RunsToDelete(runs, manifest.CollectedAt, days))
            {
                await store.DeleteRunAsync(run, cancellationToken);
            }
        }

        return stored.Map(run => new CollectedRun(instance, output, manifest, run));
    }

    private Result<IReadOnlyDictionary<string, Secret>> Resolve(CollectorInstance instance) =>
        Result.Combine(instance.Secrets.Select(pair => secrets.Resolve(pair.Value).Map(secret => KeyValuePair.Create(pair.Key, secret))))
            .Map(pairs => (IReadOnlyDictionary<string, Secret>)pairs.ToDictionary(StringComparer.Ordinal));
}
