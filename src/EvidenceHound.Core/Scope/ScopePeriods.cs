namespace EvidenceHound.Core.Scope;

/// <summary>What happens to an audit period when the scope is applied.</summary>
public enum PeriodEvent
{
    /// <summary>The period's audit is created, in progress, from the previous period as its baseline.</summary>
    Opens,

    /// <summary>The period ended: its audit moves to in review and waits for sign-off.</summary>
    Closes,

    /// <summary>The period ended earlier and is still in review: no sign-off in the audit file yet.</summary>
    AwaitingSignOff,

    /// <summary>The audit file records the period's sign-off: its audit is done and locked.</summary>
    SignedOff,
}

/// <summary>One period audit's event, and the audit it opens from.</summary>
public sealed record PeriodNote(string Audit, PeriodEvent Event, string? Baseline);

/// <summary>The period events of a scope plan, for its reviewer. Pure.</summary>
public static class ScopePeriods
{
    public static IReadOnlyList<PeriodNote> Notes(IReadOnlyList<ScopeObject> declared, ScopePlan plan) => [];
}
