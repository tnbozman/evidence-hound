using EvidenceHound.Core.Results;

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

    /// <summary>The person's last challenge, waiting for the agent's answer.</summary>
    public ReviewChallenge? Challenge { get; init; }

    /// <summary>The standing approval that accepts this item without a question, if one does.</summary>
    public StandingApproval? CoveredBy { get; init; }

    /// <summary>For a manual source's re-attestation with the same checks and results as its last: the source a standing approval could cover.</summary>
    public string? Renewable { get; init; }
}

/// <summary>A person's question or objection to an item, kept until the item is accepted.</summary>
public sealed record ReviewChallenge(string Note, string By, string On);

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
    /// <summary>
    /// What an agent may recommend for an item: a supplier review's conclusion, or accept or decline. Manual evidence takes none here;
    /// its proposal (<c>hound manual propose</c>) is the recommendation.
    /// </summary>
    public static IReadOnlyList<string> Answers(ReviewKind kind) => kind switch
    {
        ReviewKind.SupplierReview => ["ok", "warning", "blocker"],
        ReviewKind.ManualEvidence => [],
        _ => ["accept", "decline"],
    };

    /// <summary>An agent's recommendation for an item waiting in <paramref name="items"/>, checked: the item, the answer it takes, a reason and a source.</summary>
    public static Result<ReviewRecommendation> Recommend(IEnumerable<ReviewItem> items, string key, string? answer, string? reason, IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sources);
        if (items.FirstOrDefault(i => i.Key == key) is not { } item)
        {
            return Result.Fail<ReviewRecommendation>(new Problem("review.item_unknown", $"'{key}' is not waiting on a person; list what is with hound review pending --json."));
        }

        var answers = Answers(item.Kind);
        if (answers.Count == 0)
        {
            return Result.Fail<ReviewRecommendation>(new Problem("review.answer_invalid", $"'{key}' is manual evidence: its proposal is the recommendation; record it with hound manual propose."));
        }

        Problem[] problems =
        [
            .. answer is not null && answers.Contains(answer, StringComparer.Ordinal)
                ? []
                : new[] { new Problem("review.answer_invalid", $"'{key}' takes {string.Join(" or ", answers)}, not '{answer}'.") },
            .. string.IsNullOrWhiteSpace(reason) ? new[] { new Problem("review.reason_missing", "Give the reason with --reason: what the person should know to agree or disagree.") } : [],
            .. sources.Count == 0 ? new[] { new Problem("review.source_missing", "Give at least one --source: the file, page or extract the recommendation rests on.") } : [],
        ];
        return problems.Length > 0 ? Result.Fail<ReviewRecommendation>(problems) : Result.Ok(new ReviewRecommendation(answer!, reason!, sources));
    }

    public static IReadOnlyList<ReviewItem> Order(IEnumerable<ReviewItem> items) =>
        [.. items.OrderBy(i => i.Deadline ?? DateOnly.MaxValue).ThenBy(i => i.Kind).ThenBy(i => i.Key, StringComparer.Ordinal)];

    /// <summary>The items that fit in <paramref name="minutes"/>, in order; the first always, so a short session still does something.</summary>
    public static IReadOnlyList<ReviewItem> Within(IReadOnlyList<ReviewItem> ordered, int minutes)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        var taken = new List<ReviewItem>();
        var used = 0;
        foreach (var item in ordered)
        {
            if (taken.Count > 0 && used + item.Minutes > minutes)
            {
                break;
            }

            taken.Add(item);
            used += item.Minutes;
        }

        return taken;
    }
}
