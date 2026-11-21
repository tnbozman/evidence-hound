using EvidenceHound.Collectors;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Runtime;

/// <summary>A collector's result with its evidence read into memory, as stored.</summary>
public sealed record CollectorOutput(IReadOnlyList<CheckResult> Checks, IReadOnlyList<CollectedFile> Files, IReadOnlyList<ControlStatus> ControlStatuses);

/// <summary>What the runner hands every collector besides its own config: loggers and HTTP clients.</summary>
public sealed record CollectorServices(ILoggerFactory Loggers, IHttpClientFactory HttpClients);

/// <summary>One collector's stored run: what it reported, and where its evidence went.</summary>
public sealed record CollectedRun(CollectorInstance Instance, CollectorOutput Output, EvidenceManifest Manifest, StoredRun Stored);

/// <summary>Collects from one collector instance and stores its evidence: resolve secrets, collect, build the manifest, write, apply retention.</summary>
public sealed class CollectStep(ISecretProvider secrets, IEvidenceStore store, IClock clock, int? retentionDays, CollectorServices services)
{
    public Task<Result<CollectedRun>> RunAsync(CollectorInstance instance, ICollector collector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(collector);

        var now = clock.UtcNow;
        return Resolve(instance)
            .BindAsync(resolved => collector.CollectAsync(Context(instance, resolved), cancellationToken))
            .BindAsync(async result => Result.Ok(await ReadAsync(result, cancellationToken)))
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

    private CollectorContext Context(CollectorInstance instance, IReadOnlyDictionary<string, Secret> resolved) => new(
        instance.Name,
        new CollectorSettings(instance.Settings),
        resolved,
        clock,
        services.Loggers.CreateLogger($"hound.collector.{instance.Type}"),
        services.HttpClients);

    private static async Task<CollectorOutput> ReadAsync(CollectionResult result, CancellationToken cancellationToken)
    {
        var files = new List<CollectedFile>(result.Files.Count);
        foreach (var file in result.Files)
        {
            await using var content = file.Open();
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            files.Add(new CollectedFile(file.FileName, file.MediaType, buffer.ToArray()));
        }

        return new CollectorOutput(result.Checks, files, result.ControlStatuses);
    }

    private Result<IReadOnlyDictionary<string, Secret>> Resolve(CollectorInstance instance) =>
        Result.Combine(instance.Secrets.Select(pair => secrets.Resolve(pair.Value).Map(secret => KeyValuePair.Create(pair.Key, secret))))
            .Map(pairs => (IReadOnlyDictionary<string, Secret>)pairs.ToDictionary(StringComparer.Ordinal));
}
