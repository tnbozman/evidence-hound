namespace EvidenceHound.Core.Scope;

/// <summary>One supplier's solution that handles a kind of sensitive data, with the systems that hold that kind through it.</summary>
public sealed record SensitiveDataHolder(string Supplier, string SupplierName, string Solution, string SolutionName, SensitiveData Data, IReadOnlyList<string> Systems);

/// <summary>Who handles a kind of sensitive data, and on what authority: the list a prime or an auditor asks for. Pure.</summary>
public static class SensitiveDataList
{
    /// <summary>The kind a scope file names, such as <c>export_controlled</c>, or null when it is not one.</summary>
    public static SensitiveDataKind? Kind(string code) =>
        Enum.GetValues<SensitiveDataKind>().Cast<SensitiveDataKind?>().FirstOrDefault(k => SensitiveData.Code(k!.Value) == code);

    public static IReadOnlyList<SensitiveDataHolder> Handling(ScopeDocument scope, SensitiveDataKind kind)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).Where(s => s.Data.Any(d => d.Kind == kind)).ToArray();
        return
        [
            .. from entity in scope.ThirdParties
               from solution in entity.Solutions
               from data in solution.Data.Where(d => d.Kind == kind)
               select new SensitiveDataHolder(
                   entity.RefId, entity.Name, solution.RefId, solution.Name, data,
                   [.. systems.Where(s => s.Solution?.Value == solution.RefId || (s.Solution is null && s.SuppliedThrough?.Value == entity.RefId)).Select(s => s.Name)]),
        ];
    }
}
