using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Planning;

/// <summary>How many of an audit's rows a plan writes, how many it verified unchanged, and how many of the written rows restore a UI edit.</summary>
public sealed record AuditChanges(string ComplianceAssessmentRefId, int Changed, int Unchanged, int Drift);

/// <summary>The updates that differ from the current state, the counts per audit, and a <c>publish.drift_corrected</c> notice per restored row.</summary>
public sealed record ChangePlan(IReadOnlyList<AuditUpdate> Updates, IReadOnlyList<AuditChanges> Audits, IReadOnlyList<Problem> Drift);
