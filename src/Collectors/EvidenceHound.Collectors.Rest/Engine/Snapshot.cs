using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>What one query gave in a run: its items, or why it could not be read.</summary>
public abstract record QueryResult(string Name)
{
    /// <summary>Every item, read across all pages and calls. <see cref="Request"/> is the request as written in evidence, secrets left as placeholders.</summary>
    public sealed record Collected(string Name, JsonArray Items, int Pages, int Calls, string Request) : QueryResult(Name)
    {
        /// <summary>Property names that identify people, devices or networks, hashed in evidence.</summary>
        public IReadOnlyList<string> Identities { get; init; } = [];

        public bool IsBootstrap { get; init; }

        /// <summary>The first answer's whole body, where variables are read from. Not saved as evidence.</summary>
        public JsonNode? First { get; init; }
    }

    /// <summary>The API answered with a status the profile lists in <c>optional_on</c>, such as a missing licence.</summary>
    public sealed record Unavailable(string Name, string Reason) : QueryResult(Name);

    /// <summary>The query could not be read completely; nothing built from it can be assessed.</summary>
    public sealed record Failed(string Name, string Reason) : QueryResult(Name);
}

/// <summary>Every query's result in one run, and the variables queries set. Checks only ever see this snapshot.</summary>
public sealed record Snapshot(IReadOnlyDictionary<string, QueryResult> Queries, IReadOnlyDictionary<string, string> Vars);
