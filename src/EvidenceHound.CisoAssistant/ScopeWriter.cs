using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Writes scope changes to CISO Assistant: domains as folders, perimeters, and systems as assets.</summary>
internal sealed class ScopeWriter(HttpClient http) : IScopeWriter
{
    public Task<Result<Unit>> WriteAsync(ScopeWrite write, CancellationToken cancellationToken) => throw new NotImplementedException(http.BaseAddress?.ToString());
}
