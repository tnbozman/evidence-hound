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
    public static IReadOnlyList<OwedReport> Owed(IReadOnlyList<ReportingObligation> obligations, IReadOnlyList<ScopeSystem> systems, IncidentFacts incident, DateTimeOffset now) => [];
}
