namespace EvidenceHound.Core.Scope;

/// <summary>Plans the writes that bring CISO Assistant in line with a scope. Pure.</summary>
public static class ScopePlanner
{
    /// <summary>
    /// Compares the <paramref name="declared"/> objects with the <paramref name="actual"/> ones. With <paramref name="prune"/>, managed objects the
    /// scope no longer declares are deleted; unmanaged objects never are.
    /// </summary>
    public static ScopePlan Plan(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, bool prune) => throw new NotImplementedException();
}
