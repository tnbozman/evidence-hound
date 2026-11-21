using System.Collections.Immutable;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>
/// Sends planned audit updates to CISO Assistant in plan order. Every item is attempted; a failed item is
/// reported and the rest go on. Evidence whose latest revision already has the same SHA-256 is skipped, and
/// requirement updates cite the CISO Assistant IDs of the evidence uploaded earlier in the plan, or else the IDs the plan already knew.
/// An update that names its rows writes those rows; one that does not is matched to the audit's rows here.
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
        var audits = new Dictionary<string, Result<RemoteAudit>>(StringComparer.Ordinal);
        foreach (var update in updates)
        {
            var (outcome, resolved) = await update.Match(
                async upload =>
                {
                    var (uploaded, ids) = await UploadAsync(upload, evidenceIds, cancellationToken);
                    return (uploaded.Map(o => new PublishDone(o)), ids);
                },
                requirement => WithIds(UpdateRequirementAsync(requirement, evidenceIds, audits, cancellationToken), evidenceIds),
                async control => ((await UpdateControlAsync(control, cancellationToken)).Map(o => new PublishDone(o)), evidenceIds));
            evidenceIds = resolved;
            items.Add(PublishItem.For(update, outcome));
        }

        return new PublishReport(items);
    }

    private async Task<(Result<PublishOutcome> Outcome, ImmutableDictionary<string, string> EvidenceIds)> UploadAsync(
        AuditUpdate.UploadEvidence upload, ImmutableDictionary<string, string> evidenceIds, CancellationToken cancellationToken)
    {
        var evidence = upload.Evidence;
        var remote = await client.FindOrCreateEvidenceAsync(evidence.Id, EvidenceNames.For(evidence), cancellationToken);
        var outcome = await remote.Match(
            found => found.LatestSha256 == evidence.Sha256
                ? Task.FromResult(Result.Ok(PublishOutcome.Skipped))
                : SendRevisionAsync(found.Id, evidence, cancellationToken),
            problems => Task.FromResult(Result.Fail<PublishOutcome>(problems)));
        var linked = await outcome.Bind(_ => remote).BindAsync(found => LinkAsync(found.Id, upload.AppliedControlRefIds, cancellationToken));
        var final = linked.Bind(_ => outcome);
        var ids = final.Bind(_ => remote).Match(found => evidenceIds.SetItem(evidence.Id, found.Id), _ => evidenceIds);
        return (final, ids);
    }

    private async Task<Result<Unit>> LinkAsync(string evidenceId, IReadOnlyList<string> controlRefIds, CancellationToken cancellationToken)
    {
        foreach (var controlRefId in controlRefIds)
        {
            var linked = await client.LinkEvidenceToAppliedControlAsync(controlRefId, evidenceId, cancellationToken);
            if (linked is Result<Unit>.Fail)
            {
                return linked;
            }
        }

        return Result.Ok(Unit.Value);
    }

    private async Task<Result<PublishOutcome>> SendRevisionAsync(string evidenceId, Evidence evidence, CancellationToken cancellationToken)
    {
        var content = await EvidenceContent.ReadVerifiedAsync(files, evidence, cancellationToken);
        return await content.Match(
            async bytes => (await client.UploadEvidenceRevisionAsync(evidenceId, new EvidenceFile(evidence.FileName, bytes), cancellationToken)).Map(_ => PublishOutcome.Sent),
            problems => Task.FromResult(Result.Fail<PublishOutcome>(problems)));
    }

    private async Task<Result<PublishDone>> UpdateRequirementAsync(
        AuditUpdate.UpdateRequirementAssessment requirement,
        ImmutableDictionary<string, string> evidenceIds,
        Dictionary<string, Result<RemoteAudit>> audits,
        CancellationToken cancellationToken)
    {
        var cited = requirement.EvidenceRefIds.Select(refId => (RefId: refId, Id: evidenceIds.GetValueOrDefault(refId) ?? requirement.KnownEvidenceIds.GetValueOrDefault(refId))).ToList();
        var unresolved = cited.Where(c => c.Id is null).Select(c => c.RefId).ToList();
        if (unresolved.Count > 0)
        {
            return Result.Fail<PublishDone>(new Problem(
                "publish.evidence_unresolved",
                $"Requirement {requirement.RequirementRefId} cites evidence {string.Join(", ", unresolved)}, which was not uploaded to CISO Assistant in this run."));
        }

        var values = new RequirementAssessmentValues(requirement.Result, requirement.Observation, [.. cited.Select(c => c.Id!)]);
        if (requirement.RowIds.Count > 0)
        {
            return await UpdateRowsAsync(requirement.RowIds, values, cancellationToken);
        }

        var auditRefId = requirement.ComplianceAssessmentRefId;
        if (!audits.TryGetValue(auditRefId, out var audit))
        {
            audit = await client.FindAuditAsync(auditRefId, cancellationToken);
            audits[auditRefId] = audit;
        }

        var found = await client.FindRequirementAssessmentsAsync(auditRefId, requirement.Requirement, cancellationToken);
        var rows = audit.Bind(a => found.Bind(all => RequirementRows.InScope(requirement.Requirement, all, a.SelectedGroups)));
        return await rows.Match(
            inScope => inScope.Count == 0
                ? Task.FromResult(Result.Ok(new PublishDone(PublishOutcome.Skipped, RequirementRows.OutsideSelectedGroups)))
                : UpdateRowsAsync([.. inScope.Select(row => row.Id)], values, cancellationToken),
            problems => Task.FromResult(Result.Fail<PublishDone>(problems)));
    }

    private async Task<Result<PublishDone>> UpdateRowsAsync(IReadOnlyList<string> rowIds, RequirementAssessmentValues values, CancellationToken cancellationToken)
    {
        foreach (var rowId in rowIds)
        {
            var updated = await client.UpdateRequirementAssessmentAsync(rowId, values, cancellationToken);
            if (updated is Result<Unit>.Fail failed)
            {
                return Result.Fail<PublishDone>(failed.Errors);
            }
        }

        return Result.Ok(new PublishDone(PublishOutcome.Sent));
    }

    private async Task<Result<PublishOutcome>> UpdateControlAsync(AuditUpdate.UpdateAppliedControl control, CancellationToken cancellationToken) =>
        (await client.UpdateAppliedControlAsync(control.RefId, control.Status, cancellationToken)).Map(_ => PublishOutcome.Sent);

    private static async Task<(Result<PublishDone> Outcome, ImmutableDictionary<string, string> EvidenceIds)> WithIds(
        Task<Result<PublishDone>> outcome, ImmutableDictionary<string, string> evidenceIds) => (await outcome, evidenceIds);
}
