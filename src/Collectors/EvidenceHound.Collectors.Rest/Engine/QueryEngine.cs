using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Collect phase: runs every query of a profile once, as a dependency graph, and returns the snapshot.</summary>
public sealed class QueryEngine(HttpClient http, IClock clock, IDelay delay)
{
    public (HttpClient, IClock, IDelay) Ports { get; } = (http, clock, delay);

    public Task<Snapshot> CollectAsync(Profile profile, InstanceValues values, CancellationToken cancellationToken) => throw new NotImplementedException();
}
