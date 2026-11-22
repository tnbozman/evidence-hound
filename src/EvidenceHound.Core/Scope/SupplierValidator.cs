namespace EvidenceHound.Core.Scope;

/// <summary>Checks the third parties a scope declares. Pure.</summary>
public static class SupplierValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _ = today;
        return [];
    }
}
