using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>A framework's mitigation, such as application control: its code and the requirements under it.</summary>
public sealed record Mitigation(string Code, IReadOnlyList<SnapshotNode> Requirements);

/// <summary>A row to write in a system's audit: the requirement, by <c>ref</c> or <c>urn</c>, and why it is not applicable there.</summary>
public sealed record CapabilityRow(string Key, string Requirement, string Observation);

/// <summary>The rows the map writes into one system's own audit file.</summary>
public sealed record AuditCapabilityRows(string AuditRefId, string File, IReadOnlyList<CapabilityRow> Rows);

/// <summary>A mitigation no product provides: its requirements, which no system's audit answers.</summary>
public sealed record CapabilityGap(string Mitigation, IReadOnlyList<string> Requirements);

public sealed record CapabilityRows(IReadOnlyList<AuditCapabilityRows> Audits, IReadOnlyList<CapabilityGap> Gaps);

/// <summary>
/// The capability map against the framework and the scope: every mitigation decided once, and the not-applicable rows it puts in each
/// system's own audit. Pure.
/// </summary>
public static class CapabilityMap
{
    public static IReadOnlyList<Mitigation> Mitigations(FrameworkSnapshot snapshot) => throw new NotImplementedException();

    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots) => [];

    public static CapabilityRows Rows(ScopeDocument scope, FrameworkSnapshot snapshot) => new([], []);
}
