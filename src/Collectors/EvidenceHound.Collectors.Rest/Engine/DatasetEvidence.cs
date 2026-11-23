using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// One evidence file per dataset: the query files it was built from with their SHA-256, how many rows matched nothing in a required
/// join, and every row, so each check's result can be reproduced from the evidence alone.
/// </summary>
public static class DatasetEvidence
{
    public static string FileName(string dataset) => $"dataset.{dataset}.json";

    /// <summary>The dataset's file; <paramref name="queryFiles"/> are the saved query files by query name.</summary>
    public static Result<CollectedEvidence> File(RowSource.Rows dataset, IReadOnlyDictionary<string, CollectedEvidence> queryFiles, IReadOnlyCollection<Secret> secretsInUse)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(queryFiles);
        var content = new JsonObject
        {
            ["dataset"] = dataset.Name,
            ["sources"] = new JsonArray([.. dataset.Queries.Select(q => (JsonNode)new JsonObject
            {
                ["query"] = q,
                ["file"] = QueryEvidence.FileName(q),
                ["sha256"] = queryFiles.TryGetValue(q, out var file) ? QueryEvidence.Sha256(file) : null,
            })]),
            ["row_count"] = dataset.Items.Count,
            ["unmatched"] = dataset.Unmatched,
            ["rows"] = dataset.Items.DeepClone(),
        };
        return QueryEvidence.Save(FileName(dataset.Name), content, secretsInUse, $"dataset '{dataset.Name}'");
    }
}
