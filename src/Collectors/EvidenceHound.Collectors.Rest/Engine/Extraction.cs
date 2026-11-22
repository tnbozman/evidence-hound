using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Engine;

public static class Extraction
{
    public static IReadOnlyDictionary<string, RowSource> Build(Profile profile, IReadOnlyDictionary<string, QueryResult> queries, bool includeIdentities) =>
        throw new NotImplementedException();
}
