using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One evidence file per query.</summary>
public static class QueryEvidence
{
    public static Result<CollectedEvidence> File(QueryResult.Collected query, bool includeIdentities, IReadOnlyCollection<Secret> secretsInUse) => throw new NotImplementedException();

    public static string Hash(string value) => throw new NotImplementedException();
}
