using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>Reads one incident from CISO Assistant by its ref_id (s15-07). Read only.</summary>
public interface IIncidentSource
{
    Task<Result<IncidentFacts>> ReadAsync(string refId, CancellationToken cancellationToken);
}
