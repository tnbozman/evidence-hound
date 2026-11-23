namespace EvidenceHound.Core.Scope;

/// <summary>A supplier matter for the committee: an approval that lapsed, or a tier that differs from the one the organisation forces.</summary>
public sealed record SupplierNote(string Code, string Supplier, string? System, string Detail);

/// <summary>Systems refused until their suppliers are approved, and notes on suppliers already in use.</summary>
public sealed record SupplierReviewResult(IReadOnlyList<ScopeProblem> Refused, IReadOnlyList<SupplierNote> Notes);

public static class SupplierReview
{
    public static SupplierReviewResult Check(ScopeDocument scope, IReadOnlyList<ActualObject> actual, DateOnly today) => throw new NotImplementedException();
}
