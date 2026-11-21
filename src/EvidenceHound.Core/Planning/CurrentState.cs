using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Planning;

/// <summary>What CISO Assistant holds now for the audits, evidence and applied controls a plan touches. Anything left out is unknown, and is sent.</summary>
public sealed record CurrentState(IReadOnlyList<CurrentAudit> Audits, IReadOnlyList<CurrentEvidence> Evidence, IReadOnlyList<CurrentControl> Controls)
{
    /// <summary>Nothing known: every update is planned, as before change-only publishing.</summary>
    public static CurrentState Empty { get; } = new([], [], []);
}

/// <summary>An audit, the implementation groups it selects (none selects all) and every one of its rows.</summary>
public sealed record CurrentAudit(string RefId, IReadOnlyList<string> SelectedGroups, IReadOnlyList<CurrentRow> Rows);

/// <summary>One requirement assessment as CISO Assistant holds it. <paramref name="EvidenceIds"/> are CISO Assistant IDs.</summary>
public sealed record CurrentRow(
    string Id, string Urn, string? RefId, IReadOnlyList<string> Groups, ComplianceResult Result, string Observation, IReadOnlyList<string> EvidenceIds);

/// <summary>An evidence item: its ref_id, CISO Assistant ID and the SHA-256 of its latest revision, if it has one.</summary>
public sealed record CurrentEvidence(string RefId, string Id, string? LatestSha256);

/// <summary>An applied control's status.</summary>
public sealed record CurrentControl(string RefId, ControlState Status);
