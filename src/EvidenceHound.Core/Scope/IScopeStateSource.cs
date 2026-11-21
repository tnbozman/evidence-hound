using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What to read: the root folder, the frameworks the scope loads, and the rows of each audit that has hand-written results.</summary>
public sealed record ScopeStateQuery(string RootFolder, IReadOnlyList<string> FrameworkUrns, IReadOnlyDictionary<string, IReadOnlyList<RequirementKey>> Rows)
{
    public static ScopeStateQuery For(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new(
            scope.Setup.RootFolder,
            [.. scope.Setup.Frameworks.Select(f => f.Urn)],
            scope.Audits.Where(a => a.Assessments.Count > 0)
                .ToDictionary(a => a.RefId, a => (IReadOnlyList<RequirementKey>)[.. a.Assessments.Select(h => h.Requirement.Key)], StringComparer.Ordinal));
    }
}

/// <summary>Reads what CISO Assistant holds under the root folder, as objects the plan can compare with the scope. Read-only.</summary>
public interface IScopeStateSource
{
    Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken);
}
