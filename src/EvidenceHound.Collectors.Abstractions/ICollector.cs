using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors;

/// <summary>
/// Collects check results and evidence from one kind of system. A collector only talks to its target system: it never sees
/// CISO Assistant, the evidence store or the mapping. Register it with <c>services.AddCollector&lt;T&gt;()</c>.
/// </summary>
public interface ICollector
{
    /// <summary>The type name used in the deployment config, for example <c>fake</c>. Unique among registered collectors.</summary>
    string Type { get; }

    /// <summary>Recorded in each run's manifest.</summary>
    string Version { get; }

    /// <summary>The check IDs this collector can report and the settings and secrets it needs. Used to validate config.</summary>
    CollectorDescription Describe();

    /// <summary>Collects once. Expected failures (refused credentials, unreachable system) are returned as problems, not thrown.</summary>
    Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken);
}
