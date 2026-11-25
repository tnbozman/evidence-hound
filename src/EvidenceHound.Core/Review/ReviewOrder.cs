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

    /// <summary>A finding the scope reports as a warning, such as sensitive data held where nothing approves it: listed each week until fixed.</summary>
    Finding,
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

/// <summary>One group of the review queue: a kind and the recommended answer, with how many items, minutes and high-risk items.</summary>
public sealed record ReviewGroup(ReviewKind Kind, string Recommendation, int Count, int Minutes, int High);

/// <summary>A person's question or objection to an item, kept until the item is accepted.</summary>
public sealed record ReviewChallenge(string Note, string By, string On);

/// <summary>The one review queue. Pure.</summary>
public static class ReviewOrder
{
    /// <summary>The agent skill that drafts a recommendation for an item (s18-02), as the skill "weekly-review" assigns them.</summary>
    public static string Skill(ReviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ReviewKind.Capability or ReviewKind.NotApplicable or ReviewKind.System => "onboard-a-system-with-hound",
            ReviewKind.Supplier => "onboard-a-third-party",
            ReviewKind.SupplierReview => "assess-a-supplier",
            ReviewKind.Audit => "onboard-an-audit",
            ReviewKind.ImportedRow => "import-an-audit-workbook",
            ReviewKind.ManualEvidence => "process-manual-evidence",
            _ when item.Key.StartsWith("incident_report:", StringComparison.Ordinal) => "respond-to-an-incident",
            _ => "weekly-review",
        };
    }

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

    /// <summary>
    /// <c>high</c> for a decision that most needs the person's own eyes: a requirement ruled not applicable, a supplier let in, a finding, or
    /// an item whose recommendation is missing or rests on no source. Else <c>normal</c>.
    /// </summary>
    public static string Risk(ReviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind is ReviewKind.NotApplicable or ReviewKind.Supplier or ReviewKind.Finding || item.Recommendation is not { Sources.Count: > 0 }
            ? "high"
            : "normal";
    }

    /// <summary>The queue grouped by kind and recommended answer (<c>none</c> when there is none): the count and minutes of each, largest first.</summary>
    public static IReadOnlyList<ReviewGroup> Summary(IEnumerable<ReviewItem> items) =>
        [.. items.GroupBy(i => (i.Kind, Answer: i.Recommendation?.Answer ?? "none"))
            .Select(g => new ReviewGroup(g.Key.Kind, g.Key.Answer, g.Count(), g.Sum(i => i.Minutes), g.Count(i => Risk(i) == "high")))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Kind).ThenBy(g => g.Recommendation, StringComparer.Ordinal)];

    /// <summary>Up to <paramref name="count"/> of <paramref name="keys"/> nearest to <paramref name="key"/> by edit distance, for a key mistyped.</summary>
    public static IReadOnlyList<string> Nearest(IEnumerable<string> keys, string key, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(key);
        return [.. keys.OrderBy(k => Distance(k, key)).ThenBy(k => k, StringComparer.Ordinal).Take(count)];
    }

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }

    /// <summary>Days before its due date that a supplier review joins the queue: two weekly sessions to do it in.</summary>
    public static int ReviewLeadDays => 14;

    /// <summary>Whether a session can act on the item now: a supplier review from <see cref="ReviewLeadDays"/> before it is due, anything else always.</summary>
    public static bool Ready(ReviewItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind != ReviewKind.SupplierReview || item.Deadline is not { } due || due <= today.AddDays(ReviewLeadDays);
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
