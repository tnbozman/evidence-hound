namespace EvidenceHound.Core.Scope;

/// <summary>
/// A risk study (CISO Assistant's risk assessment) declared in <c>scope/risks/&lt;study&gt;.yaml</c> (s15-02): rated on one risk matrix, by URN,
/// in a domain and optionally a perimeter. Approval of accepted risks stays in CISO Assistant.
/// </summary>
public sealed record ScopeRiskStudy(
    string RefId,
    string Name,
    ScopeRef Domain,
    ScopeRef? Perimeter,
    ScopeRef Matrix,
    string? Status,
    IReadOnlyList<ScopeRef> Authors,
    IReadOnlyList<ScopeRef> Reviewers,
    string? Eta,
    string? DueDate,
    string? Description,
    IReadOnlyList<ScopeRiskScenario> Scenarios,
    SourceLine At);

/// <summary>
/// A risk scenario: the systems it affects, its threats (by URN or <c>ref_id</c>), the controls in place and planned (by <c>ref_id</c> from
/// controls.yaml), its ratings by the matrix's level names, and its treatment and justification. Risk levels are never declared.
/// </summary>
public sealed record ScopeRiskScenario(
    string RefId,
    string Name,
    string? Description,
    IReadOnlyList<ScopeRef> Systems,
    IReadOnlyList<ScopeRef> Threats,
    IReadOnlyList<ScopeRef> ExistingControls,
    IReadOnlyList<ScopeRef> PlannedControls,
    RiskRating Current,
    RiskRating Residual,
    RiskRating? Inherent,
    string Treatment,
    string? Justification,
    SourceLine At);

/// <summary>A probability and impact by level name; <see cref="NotRatedName"/> (<c>--</c>) or null when not rated.</summary>
public sealed record RiskRating(string? Probability, string? Impact)
{
    public const string NotRatedName = "--";

    public static RiskRating NotRated { get; } = new(null, null);

    /// <summary>Where the rating was written, for problems with it.</summary>
    public SourceLine? At { get; init; }
}

/// <summary>
/// A security exception requested in <c>scope/exceptions.yaml</c> (s15-03): what it covers (systems, controls, risk scenarios as
/// <c>study/scenario</c>, and audit rows), its severity, owners and expiry. Its approval status is never declared: an approver sets it in
/// CISO Assistant.
/// </summary>
public sealed record ScopeExceptionRequest(
    string RefId,
    string Name,
    string? Description,
    ScopeRef Domain,
    string Severity,
    IReadOnlyList<ScopeRef> Owners,
    string? ExpirationDate,
    string? Observation,
    IReadOnlyList<ScopeRef> Systems,
    IReadOnlyList<ScopeRef> Controls,
    IReadOnlyList<ScopeRef> Scenarios,
    IReadOnlyList<ExceptionRequirement> Requirements,
    SourceLine At);

/// <summary>A requirement an exception covers, by its audit's <c>ref_id</c> and the requirement's <c>ref_id</c> or URN.</summary>
public sealed record ExceptionRequirement(string Audit, string Requirement, SourceLine At);
