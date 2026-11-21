using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Keeps personal data out of evidence: user principal names, mail addresses, people's display names and device names are
/// replaced by a short SHA-256 hash unless identities are included.
/// </summary>
internal static class Privacy
{
    public static JsonArray Minimise(JsonArray items, bool includeIdentities) => throw new NotImplementedException();

    public static string Hash(string value) => throw new NotImplementedException();
}
