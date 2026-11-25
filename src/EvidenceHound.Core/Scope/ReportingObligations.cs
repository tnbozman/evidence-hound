namespace EvidenceHound.Core.Scope;

/// <summary>
/// A report the organisation owes when an incident happens (s15-07), from <c>scope/reporting.yaml</c>: to whom, within how long of the
/// incident's discovery, and what triggers it (the data kinds of the affected systems, or every incident).
/// </summary>
public sealed record ReportingObligation(string Id, string Name, string ReportTo, Duration Within, IReadOnlyList<SensitiveDataKind> Data, bool Always, SourceLine At)
{
    public string? Reference { get; init; }
}

/// <summary>A timeline entry of an incident: when, and what it says.</summary>
public sealed record IncidentEntry(DateTimeOffset At, string Entry);

/// <summary>An incident as CISO Assistant records it: its systems by name, when it was discovered, and its timeline.</summary>
public sealed record IncidentFacts(string RefId, string Name, DateTimeOffset Discovered, IReadOnlyList<string> Systems, IReadOnlyList<IncidentEntry> Timeline);

/// <summary>One report an incident owes: its deadline, why it is owed, and when it was made, if it was.</summary>
public sealed record OwedReport(ReportingObligation Obligation, DateTimeOffset Due, string Why, DateTimeOffset? ReportedAt, bool Overdue);

/// <summary>The reports an incident owes (s15-07). Pure.</summary>
public static class ReportingObligations
{
    /// <summary>The prefix of a timeline entry that records a report as made, such as <c>Reported: dfars-7012, DIBNet report 12345</c>.</summary>
    public const string ReportedPrefix = "Reported: ";

    /// <summary>
    /// Each obligation the incident triggers, soonest deadline first: every <c>always</c> obligation, and each one whose data kinds an
    /// affected system handles. A report is made when a timeline entry starts with <see cref="ReportedPrefix"/> and its id; one not made by
    /// its deadline is overdue.
    /// </summary>
    public static IReadOnlyList<OwedReport> Owed(IReadOnlyList<ReportingObligation> obligations, IReadOnlyList<ScopeSystem> systems, IncidentFacts incident, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(obligations);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(incident);
        var affected = systems.Where(s => incident.Systems.Contains(s.Name, StringComparer.Ordinal)).ToList();
        return
        [
            .. obligations.Select(o => (Obligation: o, Why: Why(o, affected)))
                .Where(x => x.Why is not null)
                .Select(x =>
                {
                    var due = incident.Discovered.AddSeconds(x.Obligation.Within.Seconds);
                    var reported = incident.Timeline.Where(e => Records(e, x.Obligation.Id)).Select(e => (DateTimeOffset?)e.At).Min();
                    return new OwedReport(x.Obligation, due, x.Why!, reported, reported is null && now > due);
                })
                .OrderBy(r => r.Due)
                .ThenBy(r => r.Obligation.Id, StringComparer.Ordinal),
        ];
    }

    private static string? Why(ReportingObligation obligation, IReadOnlyList<ScopeSystem> affected)
    {
        var handling = affected.SelectMany(s => s.Data.Where(d => obligation.Data.Contains(d.Kind)).Select(d => $"{s.Name} handles {SensitiveData.Code(d.Kind)}")).Distinct(StringComparer.Ordinal).ToList();
        return handling.Count > 0 ? string.Join("; ", handling) : obligation.Always ? "every incident" : null;
    }

    private static bool Records(IncidentEntry entry, string id) =>
        entry.Entry.StartsWith(ReportedPrefix, StringComparison.OrdinalIgnoreCase)
        && entry.Entry[ReportedPrefix.Length..].Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } named
        && string.Equals(named, id, StringComparison.OrdinalIgnoreCase);
}
