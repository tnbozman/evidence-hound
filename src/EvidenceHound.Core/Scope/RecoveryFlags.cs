using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Scope;

/// <summary>An asset assessment as CISO Assistant holds it: its id and its recovery flags by field name.</summary>
public sealed record HeldRecovery(string Id, IReadOnlyDictionary<string, bool> Flags)
{
    /// <summary>The evidence already linked to it, by id.</summary>
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

/// <summary>One recovery flag to set on an asset assessment, the evidence of the checks behind it, and why.</summary>
public sealed record RecoveryFlagUpdate(string AssessmentKey, string AssessmentId, string Field, bool Value, IReadOnlyList<string> EvidenceRefs, string Why);

/// <summary>Recovery flags set by each run from the checks a BIA names for them (s15-05). Pure.</summary>
public static class RecoveryFlags
{
    public static IReadOnlyList<RecoveryFlagUpdate> Plan(IReadOnlyList<ScopeBia> bias, IReadOnlyList<CheckResult> latest, IReadOnlyDictionary<string, HeldRecovery> held) => [];
}
