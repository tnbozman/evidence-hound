namespace EvidenceHound.Core.Planning;

/// <summary>Where results go in CISO Assistant, taken from configuration. Never hard-coded.</summary>
/// <param name="ComplianceAssessmentRefId">The audit's ref_id.</param>
/// <param name="AppliedControlRefIds">Configuration key to applied control ref_id.</param>
public sealed record AuditTarget(string ComplianceAssessmentRefId, IReadOnlyDictionary<string, string> AppliedControlRefIds);
