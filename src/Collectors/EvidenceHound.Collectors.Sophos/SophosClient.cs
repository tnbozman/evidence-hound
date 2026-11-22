using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>Reads Sophos Central lists from the tenant's data region, following every page.</summary>
internal sealed class SophosClient(HttpClient http, SophosSession session)
{
    public Task<Result<SophosCollection>> GetAllAsync(SophosSource source, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{http.BaseAddress} {session.TenantId} {source.Name} {cancellationToken.IsCancellationRequested}");
}
