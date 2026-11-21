using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Core.Collecting;

/// <summary>Collects check results and evidence from one system. Collectors never see CISO Assistant.</summary>
public interface ICollector
{
    /// <summary>Recorded in each run's manifest.</summary>
    string Version { get; }

    Task<Result<CollectorOutput>> CollectAsync(CollectorContext context, CancellationToken cancellationToken);
}

/// <summary>What a collector is given: its instance name, plain settings, resolved secrets and the run's start time.</summary>
public sealed record CollectorContext(
    string Name,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, Secret> Secrets,
    DateTimeOffset Now);

/// <summary>
/// What a collector found. Check results cite evidence by <see cref="CollectedFile.FileName"/>; the runner turns those into evidence IDs.
/// </summary>
public sealed record CollectorOutput(IReadOnlyList<CheckResult> Checks, IReadOnlyList<CollectedFile> Files, IReadOnlyList<ControlStatus> ControlStatuses);
