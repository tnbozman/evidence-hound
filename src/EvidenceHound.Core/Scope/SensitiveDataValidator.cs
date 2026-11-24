using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks the sensitive data a scope declares: a system may hold a kind of data through a supplier's solution only when the solution lists
/// that kind, and a solution's authority must not have expired. Pure.
/// </summary>
public static class SensitiveDataValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var solutions = scope.ThirdParties.SelectMany(e => e.Solutions)
            .GroupBy(s => s.RefId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return
        [
            .. scope.Setup.Domains.SelectMany(d => d.Systems).SelectMany(system => Unauthorised(system, solutions)),
            .. solutions.Values.SelectMany(s => s.Data).Select(d => d.Authority).OfType<DataAuthority>()
                .Where(a => a.Expires < today).Select(Expired),
        ];
    }

    private static IEnumerable<ScopeProblem> Unauthorised(ScopeSystem system, Dictionary<string, ScopeSolution> solutions)
    {
        if (system.Solution is not { } reference || !solutions.TryGetValue(reference.Value, out var solution))
        {
            return [];
        }

        var approved = solution.Data.Select(d => d.Kind).ToHashSet();
        return system.Data.Where(d => !approved.Contains(d.Kind)).Select(d => new ScopeProblem(
            "data.supplier_not_authorised",
            d.At,
            $"{system.Name} holds {SensitiveData.Code(d.Kind)} data through {solution.RefId}, which does not list {SensitiveData.Code(d.Kind)} among the data it is approved for.",
            $"Add {SensitiveData.Code(d.Kind)} with its authority to {solution.RefId}'s data, or move the data off this solution."));
    }

    private static ScopeProblem Expired(DataAuthority authority) => new(
        "data.authority_expired",
        authority.At,
        $"'{authority.Name}' expired on {authority.Expires!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
        "Record the renewed authority and its new expiry, or stop using the solution for this data.");
}
