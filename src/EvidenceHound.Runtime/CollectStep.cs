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
    public async Task<Result<CollectedRun>> RunAsync(CollectorInstance instance, ICollector collector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(collector);

        var now = clock.UtcNow;
        var resolved = Resolve(instance);
        if (resolved is Result<IReadOnlyDictionary<string, Secret>>.Fail missing)
        {
            return Result.Fail<CollectedRun>(missing.Errors);
        }

        var context = new CollectorContext(instance.Name, instance.Settings, resolved.Match(s => s, _ => throw new InvalidOperationException()), now);
        var output = await collector.CollectAsync(context, cancellationToken);
        if (output is Result<CollectorOutput>.Fail failed)
        {
            return Result.Fail<CollectedRun>(failed.Errors);
        }

        var collected = output.Match(o => o, _ => throw new InvalidOperationException());
        var manifest = Manifests.Build(instance.Name, collector.Version, now, collected.Files);
        if (manifest is Result<EvidenceManifest>.Fail invalid)
        {
            return Result.Fail<CollectedRun>(invalid.Errors);
        }

        var built = manifest.Match(m => m, _ => throw new InvalidOperationException());
        var stored = await store.WriteRunAsync(built, collected.Files, cancellationToken);
        if (stored is Result<StoredRun>.Ok ok && retentionDays is { } days)
        {
            await ApplyRetentionAsync(instance.Name, now, days, cancellationToken);
        }

        return stored.Map(run => new CollectedRun(instance, collected, built, run));
    }

    private async Task ApplyRetentionAsync(string collector, DateTimeOffset now, int days, CancellationToken cancellationToken)
    {
        var runs = await store.ListRunsAsync(collector, cancellationToken);
        foreach (var run in Retention.RunsToDelete(runs, now, days))
        {
            await store.DeleteRunAsync(run, cancellationToken);
        }
    }

    private Result<IReadOnlyDictionary<string, Secret>> Resolve(CollectorInstance instance) =>
        Result.Combine(instance.Secrets.Select(pair => secrets.Resolve(pair.Value).Map(secret => KeyValuePair.Create(pair.Key, secret))))
            .Map(pairs => (IReadOnlyDictionary<string, Secret>)pairs.ToDictionary(StringComparer.Ordinal));
}
