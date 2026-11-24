namespace EvidenceHound.Core.Review;

/// <summary>The kinds of item that wait on a person, in the order a session takes them when no deadline decides.</summary>
public enum ReviewKind
{
    Capability,
    Supplier,
    SupplierReview,
    NotApplicable,
    System,
    Audit,
    ImportedRow,
    ManualEvidence,
}

/// <summary>What the agent recommends, why, and what it read.</summary>
public sealed record ReviewRecommendation(string Answer, string Reasons, IReadOnlyList<string> Sources);

/// <summary>One item waiting on a person: what it is, why it matters, what accepting it does, and how long it takes.</summary>
public sealed record ReviewItem(ReviewKind Kind, string Key, string Title, string Why, string Effect, int Minutes)
{
    public DateOnly? Deadline { get; init; }

    public ReviewRecommendation? Recommendation { get; init; }

    /// <summary>The file in the repo the item is recorded in, for the person to read.</summary>
    public string? File { get; init; }
}

/// <summary>The one review queue. Pure.</summary>
public static class ReviewOrder
{
    public static string Code(ReviewKind kind) => kind switch
    {
        ReviewKind.SupplierReview => "supplier_review",
        ReviewKind.NotApplicable => "not_applicable",
        ReviewKind.ImportedRow => "imported_row",
        ReviewKind.ManualEvidence => "manual_evidence",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Most urgent first: by deadline (none last), then by kind, then by key.</summary>
    public static IReadOnlyList<ReviewItem> Order(IEnumerable<ReviewItem> items) =>
        throw new NotImplementedException();

    /// <summary>The items that fit in <paramref name="minutes"/>, in order; the first always, so a short session still does something.</summary>
    public static IReadOnlyList<ReviewItem> Within(IReadOnlyList<ReviewItem> ordered, int minutes) => throw new NotImplementedException();
}
