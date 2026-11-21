using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Planning;

/// <summary>Works out exactly which updates to send, so publishing is a plain loop over the plan. Pure.</summary>
public static class AuditPlanner
{
    /// <summary>Plans one audit. See the overload for several.</summary>
    public static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(
        AuditTarget target,
        IReadOnlyList<RequirementResult> requirementResults,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<ControlStatus> controlStatuses) =>
        PlanUpdates([new TargetResults(target, requirementResults)], evidence, controlStatuses);

    /// <summary>
    /// Evidence uploads (by ID) first, each once however many audits cite it; then one requirement update per result, audit by audit in
    /// target order; then one update per applied control (by key), however many targets configure it.
    /// Fails if a result cites evidence that is not in <paramref name="evidence"/>, or no target configures a control key.
    /// </summary>
    public static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(
        IReadOnlyList<TargetResults> targets,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<ControlStatus> controlStatuses)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(controlStatuses);

        var evidenceIds = evidence.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var unknownEvidence = targets
            .SelectMany(t => t.Results)
            .SelectMany(r => r.EvidenceRefs.Where(id => !evidenceIds.Contains(id)).Select(id => new Problem(
                "plan.evidence_unknown",
                $"Requirement {r.RequirementRef} cites evidence {id}, which is not in this run.")));
        var controlRefIds = controlStatuses
            .Select(c => (Status: c, RefIds: targets.Select(t => t.Target.AppliedControlRefIds.GetValueOrDefault(c.ControlKey)).OfType<string>().Distinct().ToArray()))
            .ToArray();
        var unknownControls = controlRefIds
            .Where(c => c.RefIds.Length == 0)
            .Select(c => new Problem("plan.control_key_unknown", $"Control '{c.Status.ControlKey}' is not configured in the target's controls."));
        Problem[] problems = [.. unknownEvidence, .. unknownControls];
        if (problems.Length > 0)
        {
            return Result.Fail<IReadOnlyList<AuditUpdate>>(problems);
        }

        IEnumerable<AuditUpdate> uploads = evidence
            .DistinctBy(e => e.Id)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new AuditUpdate.UploadEvidence(e));
        IEnumerable<AuditUpdate> requirements = targets.SelectMany(t => t.Results
            .Select(r => new AuditUpdate.UpdateRequirementAssessment(t.Target.ComplianceAssessmentRefId, r.RequirementRef, r.Result, r.Observation, r.EvidenceRefs)
            {
                Requirement = r.Key,
            }));
        IEnumerable<AuditUpdate> controls = controlRefIds
            .OrderBy(c => c.Status.ControlKey, StringComparer.Ordinal)
            .SelectMany(c => c.RefIds.Select(refId => new AuditUpdate.UpdateAppliedControl(refId, c.Status.State)))
            .DistinctBy(u => u.RefId);

        return Result.Ok<IReadOnlyList<AuditUpdate>>([.. uploads, .. requirements, .. controls]);
    }
}
