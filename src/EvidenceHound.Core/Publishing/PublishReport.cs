using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>What happened to one planned update.</summary>
public enum PublishOutcome
{
    /// <summary>A dry run: nothing was sent.</summary>
    Planned,
    Sent,

    /// <summary>CISO Assistant already had it (evidence with the same content).</summary>
    Skipped,
    Failed,
}

/// <summary>How an update that did not fail ended, and why when the outcome alone does not say.</summary>
public sealed record PublishDone(PublishOutcome Outcome, string? Note = null);

/// <summary>One update's outcome, a description for people, and the problems if it failed.</summary>
public sealed record PublishItem(AuditUpdate Update, string Description, PublishOutcome Outcome, IReadOnlyList<Problem> Problems)
{
    /// <summary>Why an item was skipped, when there is more to say than "already there", such as "outside selected groups".</summary>
    public string? Note { get; init; }

    /// <summary>The item for an update whose sending ended in <paramref name="outcome"/>: its outcome, or Failed with the problems.</summary>
    public static PublishItem For(AuditUpdate update, Result<PublishOutcome> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return For(update, outcome.Map(done => new PublishDone(done)));
    }

    /// <summary>The item for an update whose sending ended in <paramref name="done"/>, with its note.</summary>
    public static PublishItem For(AuditUpdate update, Result<PublishDone> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var description = UpdateDescriptions.Describe(update);
        return done.Match(
            ok => new PublishItem(update, description, ok.Outcome, []) { Note = ok.Note },
            problems => new PublishItem(update, description, PublishOutcome.Failed, problems));
    }
}

/// <summary>The outcome of publishing a plan, item by item in plan order.</summary>
public sealed record PublishReport(IReadOnlyList<PublishItem> Items)
{
    public int Planned => Count(PublishOutcome.Planned);

    public int Sent => Count(PublishOutcome.Sent);

    public int Skipped => Count(PublishOutcome.Skipped);

    public int Failed => Count(PublishOutcome.Failed);

    public bool HasFailures => Failed > 0;

    /// <summary>Requirement items counted per audit, in the order the audits first appear.</summary>
    public IReadOnlyList<AuditReport> Audits =>
        [.. Items
            .Select(item => (Audit: (item.Update as AuditUpdate.UpdateRequirementAssessment)?.ComplianceAssessmentRefId, item.Outcome))
            .Where(item => item.Audit is not null)
            .GroupBy(item => item.Audit!, StringComparer.Ordinal)
            .Select(audit => new AuditReport(
                audit.Key,
                audit.Count(i => i.Outcome == PublishOutcome.Planned),
                audit.Count(i => i.Outcome == PublishOutcome.Sent),
                audit.Count(i => i.Outcome == PublishOutcome.Skipped),
                audit.Count(i => i.Outcome == PublishOutcome.Failed)))];

    private int Count(PublishOutcome outcome) => Items.Count(item => item.Outcome == outcome);
}

/// <summary>How one audit's requirement updates went.</summary>
public sealed record AuditReport(string ComplianceAssessmentRefId, int Planned, int Sent, int Skipped, int Failed);
