using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Planning;

/// <summary>Works out exactly which updates to send, so publishing is a plain loop over the plan. Pure.</summary>
public static class AuditPlanner
{
    /// <summary>Plans several audits at once.</summary>
    public static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(
        IReadOnlyList<TargetResults> targets,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<ControlStatus> controlStatuses) => throw new NotImplementedException();

    /// <summary>
    /// Evidence uploads (by ID) first, then one requirement update per result (in result order), then control updates (by key).
    /// Fails if a result cites evidence that is not in <paramref name="evidence"/> or a control key is not in the target.
    /// </summary>
    public static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(
        AuditTarget target,
        IReadOnlyList<RequirementResult> requirementResults,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<ControlStatus> controlStatuses)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(requirementResults);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(controlStatuses);

        var evidenceIds = evidence.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var unknownEvidence = requirementResults
            .SelectMany(r => r.EvidenceRefs.Where(id => !evidenceIds.Contains(id)).Select(id => new Problem(
                "plan.evidence_unknown",
                $"Requirement {r.RequirementRef} cites evidence {id}, which is not in this run.")));
        var unknownControls = controlStatuses
            .Where(c => !target.AppliedControlRefIds.ContainsKey(c.ControlKey))
            .Select(c => new Problem("plan.control_key_unknown", $"Control '{c.ControlKey}' is not configured in the target's controls."));
        Problem[] problems = [.. unknownEvidence, .. unknownControls];
        if (problems.Length > 0)
        {
            return Result.Fail<IReadOnlyList<AuditUpdate>>(problems);
        }

        IEnumerable<AuditUpdate> uploads = evidence
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new AuditUpdate.UploadEvidence(e));
        IEnumerable<AuditUpdate> requirements = requirementResults
            .Select(r => new AuditUpdate.UpdateRequirementAssessment(target.ComplianceAssessmentRefId, r.RequirementRef, r.Result, r.Observation, r.EvidenceRefs)
            {
                Requirement = r.Key,
            });
        IEnumerable<AuditUpdate> controls = controlStatuses
            .OrderBy(c => c.ControlKey, StringComparer.Ordinal)
            .Select(c => new AuditUpdate.UpdateAppliedControl(target.AppliedControlRefIds[c.ControlKey], c.State));

        return Result.Ok<IReadOnlyList<AuditUpdate>>([.. uploads, .. requirements, .. controls]);
    }
}
