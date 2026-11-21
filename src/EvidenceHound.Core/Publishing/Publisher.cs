using System.Collections.Immutable;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>
/// Sends planned audit updates to CISO Assistant in plan order. Every item is attempted; a failed item is
/// reported and the rest go on. Evidence whose latest revision already has the same SHA-256 is skipped, and
/// requirement updates cite the CISO Assistant IDs of the evidence uploaded earlier in the plan.
/// </summary>
public sealed class Publisher(ICisoAssistantClient client, IEvidenceFiles files)
{
    /// <summary>The report a publish would start from, without calling anything.</summary>
    public static PublishReport DryRun(IReadOnlyList<AuditUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return new([.. updates.Select(update => PublishItem.For(update, Result.Ok(PublishOutcome.Planned)))]);
    }

    public async Task<PublishReport> PublishAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var items = new List<PublishItem>(updates.Count);
        var evidenceIds = ImmutableDictionary<string, string>.Empty;
        foreach (var update in updates)
        {
            var (outcome, resolved) = await update.Match(
                upload => UploadAsync(upload.Evidence, evidenceIds, cancellationToken),
                requirement => WithIds(UpdateRequirementAsync(requirement, evidenceIds, cancellationToken), evidenceIds),
                control => WithIds(UpdateControlAsync(control, cancellationToken), evidenceIds));
            evidenceIds = resolved;
            items.Add(PublishItem.For(update, outcome));
        }

        return new PublishReport(items);
    }

    private async Task<(Result<PublishOutcome> Outcome, ImmutableDictionary<string, string> EvidenceIds)> UploadAsync(
        Evidence evidence, ImmutableDictionary<string, string> evidenceIds, CancellationToken cancellationToken)
    {
        var remote = await client.FindOrCreateEvidenceAsync(evidence.Id, evidence.FileName, cancellationToken);
        var outcome = await remote.Match(
            found => found.LatestSha256 == evidence.Sha256
                ? Task.FromResult(Result.Ok(PublishOutcome.Skipped))
                : SendRevisionAsync(found.Id, evidence, cancellationToken),
            problems => Task.FromResult(Result.Fail<PublishOutcome>(problems)));
        var ids = outcome.Bind(_ => remote).Match(found => evidenceIds.SetItem(evidence.Id, found.Id), _ => evidenceIds);
        return (outcome, ids);
    }

    private async Task<Result<PublishOutcome>> SendRevisionAsync(string evidenceId, Evidence evidence, CancellationToken cancellationToken)
    {
        var content = await EvidenceContent.ReadVerifiedAsync(files, evidence, cancellationToken);
        return await content.Match(
            async bytes => (await client.UploadEvidenceRevisionAsync(evidenceId, new EvidenceFile(evidence.FileName, bytes), cancellationToken)).Map(_ => PublishOutcome.Sent),
            problems => Task.FromResult(Result.Fail<PublishOutcome>(problems)));
    }

    private async Task<Result<PublishOutcome>> UpdateRequirementAsync(
        AuditUpdate.UpdateRequirementAssessment requirement, ImmutableDictionary<string, string> evidenceIds, CancellationToken cancellationToken)
    {
        var unresolved = requirement.EvidenceRefIds.Where(refId => !evidenceIds.ContainsKey(refId)).ToList();
        if (unresolved.Count > 0)
        {
            return Result.Fail<PublishOutcome>(new Problem(
                "publish.evidence_unresolved",
                $"Requirement {requirement.RequirementRefId} cites evidence {string.Join(", ", unresolved)}, which was not uploaded to CISO Assistant in this run."));
        }

        var values = new RequirementAssessmentValues(requirement.Result, requirement.Observation, [.. requirement.EvidenceRefIds.Select(refId => evidenceIds[refId])]);
        var found = await client.FindRequirementAssessmentAsync(requirement.ComplianceAssessmentRefId, requirement.RequirementRefId, cancellationToken);
        return await found.Match(
            async id => (await client.UpdateRequirementAssessmentAsync(id, values, cancellationToken)).Map(_ => PublishOutcome.Sent),
            problems => Task.FromResult(Result.Fail<PublishOutcome>(problems)));
    }

    private async Task<Result<PublishOutcome>> UpdateControlAsync(AuditUpdate.UpdateAppliedControl control, CancellationToken cancellationToken) =>
        (await client.UpdateAppliedControlAsync(control.RefId, control.Status, cancellationToken)).Map(_ => PublishOutcome.Sent);

    private static async Task<(Result<PublishOutcome> Outcome, ImmutableDictionary<string, string> EvidenceIds)> WithIds(
        Task<Result<PublishOutcome>> outcome, ImmutableDictionary<string, string> evidenceIds) => (await outcome, evidenceIds);
}
