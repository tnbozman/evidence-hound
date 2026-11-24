using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Reporting;

/// <summary>
/// s12-04: one reporting period's figures, read once from CISO Assistant and the local run store and kept as JSON, so that every report
/// is a function of snapshots and a past period's figures can be reproduced.
/// </summary>
public sealed record PostureSnapshot(
    ReportingPeriod Period,
    DateTimeOffset TakenAt,
    IReadOnlyList<ProgrammePosture> Programmes,
    IReadOnlyList<ControlItem> Controls,
    IReadOnlyList<AcceptanceItem> Acceptances,
    IReadOnlyList<RemediationItem> Remediation,
    IReadOnlyList<IncidentItem> Incidents,
    IReadOnlyList<RiskItem> Risks,
    IReadOnlyList<CollectorHealth> Collectors,
    IReadOnlyList<DueItem> Due)
{
    /// <summary>The snapshot format; goes up when a field changes meaning.</summary>
    public int Format { get; init; } = 1;
}

/// <summary>A programme (s11-01) in one period: its framework, audit of record, selected groups, every row's result, and the counts.</summary>
public sealed record ProgrammePosture(
    string Id,
    string Name,
    string FrameworkUrn,
    int FrameworkVersion,
    string Audit,
    IReadOnlyList<ImplementationGroup> Groups,
    IReadOnlyList<ParentNode> Parents,
    IReadOnlyList<RequirementPosture> Requirements,
    IReadOnlyList<ResultCount> Counts)
{
    /// <summary>The audit's link in CISO Assistant, when known.</summary>
    public string? Link { get; init; }

    /// <summary>The programme's register summary (s11-14), when it has a register.</summary>
    public RegisterSnapshot? Register { get; init; }

    /// <summary>Whether the audit was closed (signed off and locked) when the snapshot was taken, so its figures no longer change.</summary>
    public bool Closed { get; init; }
}

/// <summary>A parent node, such as an Essential Eight strategy or an AI6 practice, by URN and short name.</summary>
public sealed record ParentNode(string Urn, string Name);

/// <summary>One requirement row of the audit of record and its result.</summary>
public sealed record RequirementPosture(string Urn, string? RefId, string? ParentUrn, IReadOnlyList<string> Groups, ComplianceResult Result)
{
    public string? Link { get; init; }
}

/// <summary>Results counted for one group, either over the whole group (<see cref="ParentUrn"/> null) or under one parent node.</summary>
public sealed record ResultCount(
    string Group, string? ParentUrn, int Compliant, int PartiallyCompliant, int NonCompliant, int NotAssessed, int NotApplicable)
{
    public int Total => Compliant + PartiallyCompliant + NonCompliant + NotAssessed + NotApplicable;
}

/// <summary>A programme's register summary in a period (s11-14): its total and the counts by field and value.</summary>
public sealed record RegisterSnapshot(string Of, int Total, IReadOnlyList<RegisterCount> Counts);

/// <summary>An applied control and its status, owner and target date.</summary>
public sealed record ControlItem(string RefId, string Name, string Status, string? Owner, DateOnly? Eta)
{
    public string? Link { get; init; }
}

/// <summary>Whether an accepted item is a risk acceptance or a security exception.</summary>
public enum AcceptanceKind
{
    RiskAcceptance,
    SecurityException,
}

/// <summary>A risk acceptance or security exception: its state, who accepted it and when, and when it expires.</summary>
public sealed record AcceptanceItem(AcceptanceKind Kind, string RefId, string Name, string State, string? AcceptedBy, DateOnly? AcceptedOn, DateOnly? Expires)
{
    public string? Link { get; init; }

    /// <summary>Whether it is in force: accepted or approved, and not revoked, rejected, resolved or expired.</summary>
    public bool Active => State is "accepted" or "approved";
}

/// <summary>A remediation item (POA&amp;M-style): a finding or an applied control being put in place, with its owner and due date.</summary>
public sealed record RemediationItem(string RefId, string Name, string? Owner, DateOnly? Due, string Status, DateOnly? ClosedOn)
{
    public string? Link { get; init; }

    public bool Open => ClosedOn is null && Status is not ("closed" or "resolved" or "done" or "active" or "dismissed" or "deprecated");
}

/// <summary>An incident recorded in CISO Assistant.</summary>
public sealed record IncidentItem(string RefId, string Name, string Status, string? Severity, DateOnly? ReportedOn, DateOnly? ClosedOn)
{
    public string? Link { get; init; }
}

/// <summary>A risk scenario: its current and residual level, owner, treatment and target date.</summary>
public sealed record RiskItem(string RefId, string Name, string? CurrentLevel, string? ResidualLevel, int Rank, string? Owner, string? Treatment, DateOnly? Target)
{
    public string? Link { get; init; }

    /// <summary>Whether the residual risk is above the organisation's appetite, when CISO Assistant records it.</summary>
    public bool? AboveAppetite { get; init; }
}

/// <summary>A collector's latest run from the local run store.</summary>
public sealed record CollectorHealth(string Collector, DateTimeOffset? LastRun, string Outcome, int ChecksPassed, int ChecksFailed, IReadOnlyList<string> Problems);

/// <summary>Whether a due item is manual evidence or a supplier review.</summary>
public enum DueKind
{
    ManualEvidence,
    SupplierReview,
}

/// <summary>A task occurrence due in CISO Assistant: manual evidence or a supplier review, with its owner and due date.</summary>
public sealed record DueItem(DueKind Kind, string Name, string? Owner, DateOnly Due, string Status)
{
    public string? Link { get; init; }

    public bool Done => Status is "completed" or "cancelled";
}
