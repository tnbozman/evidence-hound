namespace EvidenceHound.Core.Scope;

/// <summary>One clause a customer contract imposes: the customer, its contract and the obligation.</summary>
public sealed record CustomerRequirement(string Customer, string CustomerName, string Contract, ScopeObligation Obligation);

/// <summary>A clause a customer flows down to a supplier's solution, and whether the supplier's contract for that solution carries it.</summary>
public sealed record FlowedClause(string Clause, string RequiredBy, bool Carried);

/// <summary>What customers require of the organisation and, through it, of its suppliers (s15-08). Pure.</summary>
public static class CustomerObligations
{
    /// <summary>Every clause a customer contract imposes, in file order.</summary>
    public static IReadOnlyList<CustomerRequirement> All(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return
        [
            .. from entity in scope.ThirdParties
               from contract in entity.Contracts.Where(c => c.Customer)
               from obligation in contract.Obligations
               select new CustomerRequirement(entity.RefId, entity.Name, contract.RefId, obligation),
        ];
    }

    /// <summary>The clauses that require a framework: the programme's targets, so a report says which customer requires what.</summary>
    public static IReadOnlyList<CustomerRequirement> Requiring(ScopeDocument scope, string frameworkUrn) =>
        [.. All(scope).Where(r => r.Obligation.Framework?.Value == frameworkUrn)];

    /// <summary>Whether a flowed-down clause reaches data of these kinds: it covers them, or it names no kind and so covers every supplier.</summary>
    public static bool Covers(ScopeObligation obligation, IEnumerable<SensitiveDataKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        return obligation.Data.Count == 0 || kinds.Any(obligation.Data.Contains);
    }

    /// <summary>
    /// The flowed-down clauses that reach a supplier's solution handling <paramref name="kind"/>, each marked carried when one of the supplier's
    /// own contracts for that solution lists it.
    /// </summary>
    public static IReadOnlyList<FlowedClause> Flowed(ScopeDocument scope, ScopeEntity supplier, string solution, SensitiveDataKind kind)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        var carried = supplier.Contracts.Where(c => !c.Customer && c.Solutions.Any(s => s.Value == solution))
            .SelectMany(c => c.Obligations).Select(o => o.Clause).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. All(scope).Where(r => r.Obligation.FlowDown && Covers(r.Obligation, [kind]))
                .Select(r => new FlowedClause(r.Obligation.Clause, r.Contract, carried.Contains(r.Obligation.Clause))),
        ];
    }
}
