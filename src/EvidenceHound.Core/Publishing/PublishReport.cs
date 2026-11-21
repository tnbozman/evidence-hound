using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>What happened to one planned update.</summary>
public enum PublishOutcome
{
    Planned,
    Sent,
    Skipped,
    Failed,
}

/// <summary>One update's outcome, a description for people, and the problems if it failed.</summary>
public sealed record PublishItem(AuditUpdate Update, string Description, PublishOutcome Outcome, IReadOnlyList<Problem> Problems);

/// <summary>The outcome of publishing a plan. Not built yet.</summary>
public sealed record PublishReport(IReadOnlyList<PublishItem> Items)
{
    public int Planned => throw new NotImplementedException();

    public int Sent => throw new NotImplementedException();

    public int Skipped => throw new NotImplementedException();

    public int Failed => throw new NotImplementedException();

    public bool HasFailures => throw new NotImplementedException();
}
