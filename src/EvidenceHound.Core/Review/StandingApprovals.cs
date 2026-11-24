using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Review;

/// <summary>A rule a person approved instead of each instance, with who signed it, when, and when it lapses.</summary>
public sealed record StandingApproval(string Rule, string Source, string ApprovedBy, DateOnly ApprovedOn, DateOnly Expires);

/// <summary>
/// Standing approvals shrink the weekly queue. The only rule: a manual source's new export with the same checks and the same results as
/// its last attestation. A rule never covers a not-applicable decision, a risk acceptance or a first approval. Pure.
/// </summary>
public static class StandingApprovals
{
    public const string UnchangedReattestation = "unchanged_reattestation";

    public static StandingApproval Unchanged(string source, string approvedBy, DateOnly today) =>
        new(UnchangedReattestation, source, approvedBy, today, today.AddMonths(12));

    public static bool Covers(
        StandingApproval rule, ReviewKind kind, string source, IReadOnlyDictionary<string, CheckOutcome> proposed, IReadOnlyDictionary<string, CheckOutcome>? last, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(proposed);
        return rule.Rule == UnchangedReattestation && kind == ReviewKind.ManualEvidence && rule.Source == source && today <= rule.Expires
            && last is not null && proposed.Count == last.Count
            && proposed.All(p => last.TryGetValue(p.Key, out var before) && before == p.Value);
    }
}
