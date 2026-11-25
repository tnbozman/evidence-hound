using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>Checks business impact analyses offline (s15-04). Pure.</summary>
public static class BiaValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, LibrarySnapshot> libraries) => throw new NotImplementedException();
}
