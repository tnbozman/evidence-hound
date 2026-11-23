using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>What a check reads: the rows of a query or dataset, or why there are none to assess.</summary>
public abstract record RowSource(string Name)
{
    /// <summary>
    /// The rows, the queries they came from (in order), and how many rows matched nothing in a required join. Identities are already
    /// hashed unless the instance includes them, so rows are exactly what the evidence holds.
    /// </summary>
    public sealed record Rows(string Name, JsonArray Items, IReadOnlyList<string> Queries, int Unmatched) : RowSource(Name);

    /// <summary>A query it depends on answered with a status the profile lists in <c>optional_on</c>.</summary>
    public sealed record Unavailable(string Name, string Reason) : RowSource(Name);

    /// <summary>A query it depends on could not be read completely.</summary>
    public sealed record Failed(string Name, string Reason) : RowSource(Name);
}
