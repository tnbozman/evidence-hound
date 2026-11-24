using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks the sensitive data a scope declares: a system may hold a kind of data through a supplier's solution only when the solution lists
/// that kind, and a solution's authority must not have expired. Both are findings about the organisation, so they warn and block nothing
/// (maintainer's decision, s10-05): the repo records the truth while the data moves or the authority is renewed. Pure.
/// </summary>
public static class SensitiveDataValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var solutions = scope.ThirdParties.SelectMany(e => e.Solutions).DistinctBy(s => s.RefId).ToDictionary(s => s.RefId, StringComparer.Ordinal);
        var entities = scope.ThirdParties.DistinctBy(e => e.RefId).ToDictionary(e => e.RefId, StringComparer.Ordinal);
        return
        [
            .. scope.Setup.Domains.SelectMany(d => d.Systems).SelectMany(system => Unauthorised(system, Supplier(system, solutions, entities))),
            .. solutions.Values.SelectMany(s => s.Data).Select(d => d.Authority).OfType<DataAuthority>()
                .Where(a => a.Expires < today).Select(Expired),
        ];
    }

    /// <summary>
    /// What a system's data passes through, and the kinds it is approved for: the solution it names, or else every solution of the third
    /// party it is supplied through.
    /// </summary>
    private static (string RefId, HashSet<SensitiveDataKind> Kinds)? Supplier(
        ScopeSystem system, Dictionary<string, ScopeSolution> solutions, Dictionary<string, ScopeEntity> entities)
    {
        if (system.Solution is { } named)
        {
            return solutions.TryGetValue(named.Value, out var solution) ? (solution.RefId, Kinds([solution])) : null;
        }

        return system.SuppliedThrough is { } through && entities.TryGetValue(through.Value, out var entity) ? (entity.RefId, Kinds(entity.Solutions)) : null;
    }

    private static HashSet<SensitiveDataKind> Kinds(IEnumerable<ScopeSolution> solutions) => [.. solutions.SelectMany(s => s.Data).Select(d => d.Kind)];

    private static IEnumerable<ScopeProblem> Unauthorised(ScopeSystem system, (string RefId, HashSet<SensitiveDataKind> Kinds)? supplier) =>
        supplier is not var (refId, kinds)
            ? []
            : system.Data.Where(d => !kinds.Contains(d.Kind)).Select(d => new ScopeProblem(
                "data.supplier_not_authorised",
                d.At,
                $"{system.Name} holds {SensitiveData.Code(d.Kind)} data through {refId}, which does not list {SensitiveData.Code(d.Kind)} among the data it is approved for.",
                $"Add {SensitiveData.Code(d.Kind)} with its authority to {refId}'s data, or move the data off it.")
            { Warning = true });

    private static ScopeProblem Expired(DataAuthority authority) => new(
        "data.authority_expired",
        authority.At,
        $"'{authority.Name}' expired on {authority.Expires!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
        "Record the renewed authority and its new expiry, or stop using the solution for this data.")
    { Warning = true };
}
