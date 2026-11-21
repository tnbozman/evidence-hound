using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What to read: the root folder, the frameworks the scope loads, and the rows of each audit that has hand-written results.</summary>
public sealed record ScopeStateQuery(string RootFolder, IReadOnlyList<string> FrameworkUrns, IReadOnlyDictionary<string, IReadOnlyList<RequirementKey>> Rows)
{
    public static ScopeStateQuery For(ScopeDocument scope) => throw new NotImplementedException();
}

/// <summary>Reads what CISO Assistant holds under the root folder, as objects the plan can compare with the scope. Read-only.</summary>
public interface IScopeStateSource
{
    Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken);
}
