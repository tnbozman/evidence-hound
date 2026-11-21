using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>One evidence file per Graph collection: the request, how many pages were read, and every item, minimised.</summary>
internal static class GraphEvidence
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static CollectedEvidence File(string fileName, GraphCollection collection, bool includeIdentities)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var content = new JsonObject
        {
            ["request"] = collection.Request.Path,
            ["pages"] = collection.Pages,
            ["identities"] = includeIdentities ? "included" : "hashed",
            ["value"] = Privacy.Minimise(collection.Items, includeIdentities),
        };
        return CollectedEvidence.FromBytes(fileName, "application/json", Encoding.UTF8.GetBytes(content.ToJsonString(Indented)));
    }
}
