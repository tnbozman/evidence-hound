using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What to read: the root folder, the frameworks and other libraries the scope loads, and the rows of each audit the scope writes.</summary>
public sealed record ScopeStateQuery(string RootFolder, IReadOnlyList<string> FrameworkUrns, IReadOnlyDictionary<string, IReadOnlyList<RequirementKey>> Rows)
{
    /// <summary>The query for a scope: its root folder, its frameworks and other libraries, and the rows its <paramref name="declared"/> objects write.</summary>
    public static ScopeStateQuery For(ScopeDocument scope, IReadOnlyList<ScopeObject> declared)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(declared);
        return new(
            scope.Setup.RootFolder,
            [.. scope.Setup.Frameworks.Select(f => f.Urn), .. scope.Setup.Libraries.Select(l => l.Value)],
            declared.Where(d => d.Kind == ScopeKind.Assessment)
                .Select(d => d.Key.Split('/', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0], StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RequirementKey>)[.. g.Select(parts => parts[1].StartsWith("urn:", StringComparison.Ordinal) ? RequirementKey.Urn(parts[1]) : RequirementKey.Ref(parts[1]))],
                    StringComparer.Ordinal));
    }
}

/// <summary>Reads what CISO Assistant holds under the root folder, as objects the plan can compare with the scope. Read-only.</summary>
public interface IScopeStateSource
{
    Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken);
}
