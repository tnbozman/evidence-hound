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

/// <summary>One update's outcome, a description for people, and the problems if it failed.</summary>
public sealed record PublishItem(AuditUpdate Update, string Description, PublishOutcome Outcome, IReadOnlyList<Problem> Problems);

/// <summary>The outcome of publishing a plan, item by item in plan order.</summary>
public sealed record PublishReport(IReadOnlyList<PublishItem> Items)
{
    public int Planned => Count(PublishOutcome.Planned);

    public int Sent => Count(PublishOutcome.Sent);

    public int Skipped => Count(PublishOutcome.Skipped);

    public int Failed => Count(PublishOutcome.Failed);

    public bool HasFailures => Failed > 0;

    private int Count(PublishOutcome outcome) => Items.Count(item => item.Outcome == outcome);
}
