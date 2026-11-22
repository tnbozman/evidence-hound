using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>
/// Signs in to Sophos Central: a client credentials token from Sophos ID, then <c>whoami</c> to learn whether the credential
/// belongs to a tenant, a partner or an organisation, and the data region API of the tenant to read.
/// </summary>
internal static class SophosAuth
{
    public static Task<Result<SophosSession>> SignInAsync(HttpClient http, SophosConnection connection, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public static bool IsTrustedRegion(Uri region, Uri global) => throw new NotImplementedException();
}
