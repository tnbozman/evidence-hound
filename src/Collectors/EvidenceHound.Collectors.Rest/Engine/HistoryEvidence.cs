using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// The earlier copies of a query's evidence a check with <c>history</c> reads, and the evidence file that records them
/// (<c>&lt;query&gt;.history.json</c>), so the result can be reproduced from this run's evidence alone.
/// </summary>
public static class HistoryEvidence
{
    public static string FileName(string query) => query + ".history.json";

    /// <summary>The items of each earlier evidence file, oldest first; a file that is not a query's evidence is left out.</summary>
    public static IReadOnlyList<EarlierRows> Rows(IEnumerable<EarlierFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return [.. files.Select(f => (f.CollectedAt, Items: Items(f.Content))).Where(f => f.Items is not null).OrderBy(f => f.CollectedAt).Select(f => new EarlierRows(f.CollectedAt, f.Items!))];
    }

    public static Result<CollectedEvidence> File(string query, IReadOnlyList<EarlierRows> earlier, IReadOnlyCollection<Secret> secretsInUse)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        var content = new JsonObject
        {
            ["query"] = query,
            ["source"] = "earlier runs of this collector instance, as the evidence store kept them",
            ["runs"] = new JsonArray([.. earlier.Select(e => (JsonNode)new JsonObject
            {
                ["collected_at"] = e.CollectedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                ["items"] = e.Items.DeepClone(),
            })]),
        };
        return QueryEvidence.Save(FileName(query), content, secretsInUse, $"the history of query '{query}'");
    }

    private static JsonArray? Items(byte[] content)
    {
        try
        {
            return JsonNode.Parse(content)?["items"] as JsonArray;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
