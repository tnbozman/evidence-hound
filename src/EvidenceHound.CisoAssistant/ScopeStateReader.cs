using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Reads what CISO Assistant holds under the scope's root folder, with GET requests only.</summary>
internal sealed class ScopeStateReader(HttpClient http) : IScopeStateSource
{
    public Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken) => throw new NotImplementedException(http.BaseAddress?.ToString());
}
