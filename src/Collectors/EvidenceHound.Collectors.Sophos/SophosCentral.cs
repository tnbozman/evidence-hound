using System.Text.Json.Nodes;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>What the collector signs in with: an API credential from Sophos Central, and where Sophos ID and the global API are.</summary>
internal sealed record SophosConnection(string ClientId, Secret ClientSecret, string? TenantId, Uri IdUrl, Uri ApiUrl);

/// <summary>A signed-in session for one tenant: the bearer token, the tenant ID and the tenant's data region API address.</summary>
internal sealed record SophosSession(string Token, string TenantId, Uri DataRegion);

/// <summary>One list to read from the tenant's data region, paged by page number: its name (also its evidence file name) and its path.</summary>
internal sealed record SophosSource(string Name, string Path)
{
    public string EvidenceFile => Name + ".json";
}

/// <summary>Every item of a list, read across all its pages.</summary>
internal sealed record SophosCollection(SophosSource Source, JsonArray Items, int Pages);
