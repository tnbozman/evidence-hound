using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>Checks risk studies offline against the matrix and threat snapshots and the rest of the scope (s15-02). Pure.</summary>
public static class RiskValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, LibrarySnapshot> libraries) => throw new NotImplementedException();
}
