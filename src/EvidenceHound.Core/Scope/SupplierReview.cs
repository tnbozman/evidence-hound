using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>A supplier matter for the committee: an approval that lapsed, or a tier that differs from the one the organisation forces.</summary>
public sealed record SupplierNote(string Code, string Supplier, string? System, string Detail);

/// <summary>Systems refused until their suppliers are approved, and notes on suppliers already in use.</summary>
public sealed record SupplierReviewResult(IReadOnlyList<ScopeProblem> Refused, IReadOnlyList<SupplierNote> Notes);

/// <summary>
/// The supplier gate, from what CISO Assistant holds. Pure. A supplier is approved when its latest concluded assessment is concluded
/// <c>ok</c>, has not expired, and no later review is overdue, and it has an <c>active</c> contract. <c>hound</c> only reads approval.
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
        var refused = new List<ScopeProblem>();
        var notes = new List<SupplierNote>();
        foreach (var system in scope.Setup.Domains.SelectMany(d => d.Systems))
        {
            string?[] named = [system.Solution is { } solution ? suppliers.GetValueOrDefault(solution.Value) : null, system.SuppliedThrough?.Value];
            foreach (var supplier in named.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (Missing(supplier, actual, today) is not { } missing)
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
                        $"Conclude {supplier}'s assessment as ok in CISO Assistant and make its contract active, then plan again."));
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

    /// <summary>What keeps <paramref name="supplier"/> from being approved, or null when it is approved.</summary>
    private static string? Missing(string supplier, IReadOnlyList<ActualObject> actual, DateOnly today)
    {
        var assessments = actual.Where(a => a.Held.Kind == ScopeKind.EntityAssessment && a.Held.Fields.GetValueOrDefault("entity") == supplier)
            .OrderByDescending(a => a.Held.Fields.GetValueOrDefault("due_date"), StringComparer.Ordinal).ToArray();
        var concluded = assessments.FirstOrDefault(a => a.Observed.ContainsKey("conclusion"));
        var reasons = new List<string>();
        if (concluded is null)
        {
            reasons.Add("no concluded assessment");
        }
        else
        {
            var conclusion = concluded.Observed["conclusion"];
            if (conclusion != "ok")
            {
                reasons.Add($"{concluded.Held.Key} concluded {conclusion}");
            }

            if (concluded.Observed.GetValueOrDefault("expiry_date") is { } expiry && Day(expiry) is { } expires && expires < today)
            {
                reasons.Add($"{concluded.Held.Key}'s conclusion expired on {expiry}");
            }

            var due = concluded.Held.Fields.GetValueOrDefault("due_date");
            reasons.AddRange(assessments
                .Where(a => !a.Observed.ContainsKey("conclusion") && string.CompareOrdinal(a.Held.Fields.GetValueOrDefault("due_date"), due) > 0
                    && Day(a.Held.Fields.GetValueOrDefault("due_date")) is { } dueDay && dueDay < today)
                .Select(a => $"review {a.Held.Key} was due on {a.Held.Fields["due_date"]} and is not concluded"));
        }

        if (!actual.Any(a => a.Held.Kind == ScopeKind.Contract && a.Held.Fields.GetValueOrDefault("entity") == supplier && a.Held.Fields.GetValueOrDefault("status") == "active"))
        {
            reasons.Add("no active contract");
        }

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
