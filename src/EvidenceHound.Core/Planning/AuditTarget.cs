using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Planning;

/// <summary>Where results go in CISO Assistant, taken from configuration. Never hard-coded.</summary>
/// <param name="ComplianceAssessmentRefId">The audit's ref_id.</param>
/// <param name="AppliedControlRefIds">Configuration key to applied control ref_id.</param>
public sealed record AuditTarget(string ComplianceAssessmentRefId, IReadOnlyDictionary<string, string> AppliedControlRefIds)
{
    /// <summary>What a result with no evidence is published as (s15-14): non-compliant unless the target opts out with not assessed.</summary>
    public ComplianceResult NoEvidence { get; init; } = ComplianceResult.NonCompliant;
}

/// <summary>One audit's target and the requirement results to publish to it.</summary>
public sealed record TargetResults(AuditTarget Target, IReadOnlyList<RequirementResult> Results);
