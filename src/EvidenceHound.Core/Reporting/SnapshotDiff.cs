using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Reporting;

/// <summary>A requirement row whose result changed between two periods; <see cref="From"/> is null for a row new in the later period.</summary>
public sealed record RequirementChange(string Programme, string Urn, string? RefId, string? ParentUrn, ComplianceResult? From, ComplianceResult To);

/// <summary>An applied control whose status changed; <see cref="From"/> is null for a control new in the later period.</summary>
public sealed record ControlChange(string RefId, string Name, string? From, string To);

/// <summary>s12-04: what changed between two snapshots. Every report is a function of snapshots and their differences.</summary>
public sealed record SnapshotDiff(
    IReadOnlyList<RequirementChange> Requirements,
    IReadOnlyList<ControlChange> Controls,
    IReadOnlyList<AcceptanceItem> NewAcceptances,
    IReadOnlyList<AcceptanceItem> ExpiredAcceptances,
    IReadOnlyList<AcceptanceItem> ClosedAcceptances)
{
    public bool IsEmpty => Requirements.Count + Controls.Count + NewAcceptances.Count + ExpiredAcceptances.Count + ClosedAcceptances.Count == 0;
}

public static class Snapshots
{
    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/>. Pure: <c>Diff(a, a)</c> is empty.</summary>
    public static SnapshotDiff Diff(PostureSnapshot before, PostureSnapshot after) => throw new NotImplementedException();
}
