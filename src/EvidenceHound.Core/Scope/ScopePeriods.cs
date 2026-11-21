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
    /// <summary>
    /// Each period audit the plan creates (opens), moves to in review (closes) or to done and locked (signed off), and each one it leaves in review
    /// (awaiting sign-off), in key order. Audits without a period status are not listed.
    /// </summary>
    public static IReadOnlyList<PeriodNote> Notes(IReadOnlyList<ScopeObject> declared, ScopePlan plan)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(plan);

        var moved = plan.Changes
            .Where(c => c.Kind == ScopeKind.Audit && (c.Action == ScopeAction.Create || c.Fields.Any(f => f.Field == "status")))
            .ToDictionary(c => c.Key, c => (ScopeAction?)c.Action, StringComparer.Ordinal);
        return
        [
            .. declared
                .Where(o => o.Kind == ScopeKind.Audit && o.Fields.ContainsKey("status"))
                .Select(o => Note(o, moved.GetValueOrDefault(o.Key)))
                .OfType<PeriodNote>()
                .OrderBy(n => n.Audit, StringComparer.Ordinal),
        ];
    }

    private static PeriodNote? Note(ScopeObject audit, ScopeAction? action) => (audit.Fields["status"], action) switch
    {
        ("in_progress", ScopeAction.Create) => new PeriodNote(audit.Key, PeriodEvent.Opens, audit.Initial.GetValueOrDefault("baseline")),
        ("in_review", null) => new PeriodNote(audit.Key, PeriodEvent.AwaitingSignOff, null),
        ("in_review", _) => new PeriodNote(audit.Key, PeriodEvent.Closes, null),
        ("done", not null) => new PeriodNote(audit.Key, PeriodEvent.SignedOff, null),
        _ => null,
    };
}
