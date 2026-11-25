using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>Reads incidents from CISO Assistant with their timelines (s15-07). Read only.</summary>
public interface IIncidentSource
{
    /// <summary>One incident by its ref_id; <c>incident.not_found</c> when CISO Assistant holds none.</summary>
    Task<Result<IncidentFacts>> ReadAsync(string refId, CancellationToken cancellationToken);

    /// <summary>Every incident not yet closed, resolved or dismissed.</summary>
    Task<Result<IReadOnlyList<IncidentFacts>>> OpenAsync(CancellationToken cancellationToken);
}
