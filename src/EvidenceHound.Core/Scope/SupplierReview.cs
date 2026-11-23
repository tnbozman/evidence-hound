using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>A supplier matter for the committee: an approval that lapsed, or a tier that differs from the one the organisation forces.</summary>
public sealed record SupplierNote(string Code, string Supplier, string? System, string Detail);

/// <summary>Systems refused until their suppliers are approved, and notes on suppliers already in use.</summary>
public sealed record SupplierReviewResult(IReadOnlyList<ScopeProblem> Refused, IReadOnlyList<SupplierNote> Notes);

/// <summary>
/// The supplier gate, from what the scope records and CISO Assistant holds. Pure. A supplier is approved when its latest concluded
/// assessment is concluded <c>ok</c>, has not expired, and no later review is overdue, and it has an <c>active</c> contract. A person
/// approves in the config repo with <c>hound approve</c>; what the scope records counts before it is applied.
/// </summary>
public static class SupplierReview
{
    public static SupplierReviewResult Check(ScopeDocument scope, IReadOnlyList<ActualObject> actual, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(actual);
        var suppliers = scope.ThirdParties.SelectMany(e => e.Solutions.Select(s => (Solution: s.RefId, Entity: e.RefId)))
            .GroupBy(s => s.Solution, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Entity, StringComparer.Ordinal);
        var registered = actual.Where(a => a.Held.Kind == ScopeKind.System).Select(a => a.Held.Key).ToHashSet(StringComparer.Ordinal);
        var declared = ScopeDesired.Reviews(scope, today);
        var refused = new List<ScopeProblem>();
        var notes = new List<SupplierNote>();
        foreach (var system in scope.Setup.Domains.SelectMany(d => d.Systems))
        {
            string?[] named = [system.Solution is { } solution ? suppliers.GetValueOrDefault(solution.Value) : null, system.SuppliedThrough?.Value];
            foreach (var supplier in named.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (Missing(supplier, scope, declared, actual, today) is not { } missing)
                {
                    continue;
                }

                if (registered.Contains(system.Name))
                {
                    notes.Add(new SupplierNote("supplier.approval_lapsed", supplier, system.Name, $"{supplier} is no longer approved: {missing}. {system.Name} stays registered; review the supplier."));
                }
                else
                {
                    refused.Add(new ScopeProblem(
                        "system.supplier_not_approved", system.At,
                        $"system '{system.Name}' cannot be registered: its supplier {supplier} is not approved ({missing}).",
                        $"Approve {supplier} and conclude its review as ok with 'hound approve' (it makes the contract active), then plan again."));
                }
            }
        }

        foreach (var entity in scope.ThirdParties.Where(e => e.ForcedTier is not null))
        {
            var held = actual.FirstOrDefault(a => a.Held.Kind == ScopeKind.Entity && a.Held.Key == entity.RefId);
            var tier = held?.Observed.GetValueOrDefault("tier");
            if (held is not null && tier != entity.ForcedTier)
            {
                notes.Add(new SupplierNote(
                    "supplier.tier_differs", entity.RefId, null,
                    $"CISO Assistant holds {entity.RefId} at {(tier is null ? "no tier" : $"tier {tier}")}; the scope forces {entity.ForcedTier}: {entity.ForcedTierReason}"));
            }
        }

        return new SupplierReviewResult(refused, notes);
    }

    /// <summary>A supplier's review as the gate sees it: what CISO Assistant holds, with the conclusion the scope records in its place.</summary>
    private sealed record Review(string Key, string? Due, string? Conclusion, string? Expiry);

    /// <summary>
    /// The supplier's reviews, latest due first. A conclusion a person recorded in the scope with <c>hound approve</c> counts before it is
    /// applied, and wins over the one CISO Assistant holds for the same review.
    /// </summary>
    private static Review[] Reviews(string supplier, IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual)
    {
        var held = actual.Where(a => a.Held.Kind == ScopeKind.EntityAssessment && a.Held.Fields.GetValueOrDefault("entity") == supplier)
            .Select(a => new Review(a.Held.Key, a.Held.Fields.GetValueOrDefault("due_date"), a.Observed.GetValueOrDefault("conclusion"), a.Observed.GetValueOrDefault("expiry_date")))
            .ToDictionary(r => r.Key, StringComparer.Ordinal);
        foreach (var review in declared.Where(d => d.Fields.GetValueOrDefault("entity") == supplier))
        {
            var conclusion = review.Fields.GetValueOrDefault("conclusion");
            held[review.Key] = held.TryGetValue(review.Key, out var known)
                ? known with { Conclusion = conclusion ?? known.Conclusion }
                : new Review(review.Key, review.Fields.GetValueOrDefault("due_date"), conclusion, null);
        }

        return [.. held.Values.OrderByDescending(r => r.Due, StringComparer.Ordinal)];
    }

    /// <summary>What keeps <paramref name="supplier"/> from being approved, or null when it is approved.</summary>
    private static string? Missing(string supplier, ScopeDocument scope, IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, DateOnly today)
    {
        var reviews = Reviews(supplier, declared, actual);
        var concluded = reviews.FirstOrDefault(r => r.Conclusion is not null);
        var reasons = new List<string>();
        if (concluded is null)
        {
            reasons.Add("no concluded assessment");
        }
        else
        {
            if (concluded.Conclusion != "ok")
            {
                reasons.Add($"{concluded.Key} concluded {concluded.Conclusion}");
            }

            if (concluded.Expiry is { } expiry && Day(expiry) is { } expires && expires < today)
            {
                reasons.Add($"{concluded.Key}'s conclusion expired on {expiry}");
            }

            reasons.AddRange(reviews
                .Where(r => r.Conclusion is null && string.CompareOrdinal(r.Due, concluded.Due) > 0 && Day(r.Due) is { } dueDay && dueDay < today)
                .Select(r => $"review {r.Key} was due on {r.Due} and is not concluded"));
        }

        var activeInScope = scope.ThirdParties.Any(e => e.RefId == supplier && e.Contracts.Any(c => c.Status == "active"));
        if (!activeInScope && !actual.Any(a => a.Held.Kind == ScopeKind.Contract && a.Held.Fields.GetValueOrDefault("entity") == supplier && a.Held.Fields.GetValueOrDefault("status") == "active"))
        {
            reasons.Add("no active contract");
        }

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
