namespace EvidenceHound.Core.Scope;

/// <summary>
/// A business impact analysis from <c>scope/bia/&lt;bia&gt;.yaml</c> (s15-04): rated on one risk matrix's impact scale, with one asset
/// assessment per system.
/// </summary>
public sealed record ScopeBia(
    string RefId,
    string Name,
    string? Description,
    ScopeRef Domain,
    ScopeRef? Perimeter,
    ScopeRef Matrix,
    string? Version,
    string? Status,
    IReadOnlyList<ScopeRef> Authors,
    IReadOnlyList<ScopeRef> Reviewers,
    string? DueDate,
    IReadOnlyList<ScopeAssetAssessment> Assessments,
    SourceLine At);

/// <summary>One system's assessment in a BIA: what it depends on, the controls that keep it running, its recovery flags and thresholds.</summary>
public sealed record ScopeAssetAssessment(
    ScopeRef System,
    IReadOnlyList<ScopeRef> Dependencies,
    IReadOnlyList<ScopeRef> Controls,
    bool Documented,
    bool Tested,
    bool TargetsMet,
    string? Observation,
    IReadOnlyList<ScopeThreshold> Thresholds,
    SourceLine At)
{
    /// <summary>The checks named for a recovery flag (<c>documented</c>, <c>tested</c>, <c>targets_met</c>), which each run sets from them (s15-05).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScopeRef>> Checks { get; init; } = new Dictionary<string, IReadOnlyList<ScopeRef>>();
}

/// <summary>An escalation threshold: after this long down, the impact (a matrix impact level name), why, and its qualifications.</summary>
public sealed record ScopeThreshold(Duration After, ScopeRef Impact, IReadOnlyList<string> Qualifications, string? Justification, SourceLine At);
