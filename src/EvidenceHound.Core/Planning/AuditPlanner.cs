using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
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
        IReadOnlyList<ControlStatus> controlStatuses) =>
        Validate(targets, evidence, controlStatuses).Map(controls => (IReadOnlyList<AuditUpdate>)
        [
            .. Uploads(evidence, _ => true),
            .. targets.SelectMany(t => t.Results.Select(r => Update(t.Target, r, r.Observation, new Dictionary<string, string>()))),
            .. controls,
        ]);

    /// <summary>
    /// Like <see cref="PlanUpdates(IReadOnlyList{TargetResults}, IReadOnlyList{Evidence}, IReadOnlyList{ControlStatus})"/>, but leaves out
    /// whatever <paramref name="current"/> already holds. A row's state is its result, its checks' outcomes and its evidence hashes; a row
    /// whose marker shows that state is unchanged, unless someone edited it since, which is drift: it is written back and reported.
    /// Evidence whose latest hash matches is cited by its known ID and not uploaded. Each written observation ends with a marker naming
    /// <paramref name="runId"/>. Rows outside the audit's selected groups are neither sent nor counted; a requirement with no known row
    /// is left to the publisher to find.
    /// </summary>
    public static Result<ChangePlan> PlanChanges(
        IReadOnlyList<TargetResults> targets,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<ControlStatus> controlStatuses,
        CurrentState current,
        string runId)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return Validate(targets, evidence, controlStatuses).Map(controls =>
        {
            var hashes = evidence.DistinctBy(e => e.Id).ToDictionary(e => e.Id, e => e.Sha256, StringComparer.Ordinal);
            var known = current.Evidence
                .Where(e => hashes.TryGetValue(e.RefId, out var sha) && e.LatestSha256 == sha)
                .DistinctBy(e => e.RefId)
                .ToDictionary(e => e.RefId, e => e.Id, StringComparer.Ordinal);
            var audits = current.Audits.DistinctBy(a => a.RefId).ToDictionary(a => a.RefId, StringComparer.Ordinal);
            var documents = current.ScopeDocumentIds.ToHashSet(StringComparer.Ordinal);
            var rows = targets
                .SelectMany(t => t.Results.Select(r => PlanRows(t.Target, audits.GetValueOrDefault(t.Target.ComplianceAssessmentRefId), r, hashes, known, documents, runId)))
                .ToArray();
            var statuses = current.Controls.DistinctBy(c => c.RefId).ToDictionary(c => c.RefId, c => c.Status, StringComparer.Ordinal);

            return new ChangePlan(
                [
                    .. Uploads(evidence, e => !known.ContainsKey(e.Id)),
                    .. rows.Select(r => r.Update).OfType<AuditUpdate>(),
                    .. controls.Where(c => !statuses.TryGetValue(c.RefId, out var status) || status != c.Status),
                ],
                [.. targets.Select(t => t.Target.ComplianceAssessmentRefId).Distinct(StringComparer.Ordinal).Select(audit => Tally(audit, rows))],
                [.. rows.SelectMany(r => r.Drift)]);
        });
    }

    private static Result<AuditUpdate.UpdateAppliedControl[]> Validate(
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
        return problems.Length > 0
            ? Result.Fail<AuditUpdate.UpdateAppliedControl[]>(problems)
            : Result.Ok(controlRefIds
                .OrderBy(c => c.Status.ControlKey, StringComparer.Ordinal)
                .SelectMany(c => c.RefIds.Select(refId => new AuditUpdate.UpdateAppliedControl(refId, c.Status.State)))
                .DistinctBy(u => u.RefId)
                .ToArray());
    }

    private static IEnumerable<AuditUpdate> Uploads(IReadOnlyList<Evidence> evidence, Func<Evidence, bool> needed) =>
        evidence
            .DistinctBy(e => e.Id)
            .Where(needed)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new AuditUpdate.UploadEvidence(e));

    private static AuditUpdate.UpdateRequirementAssessment Update(
        AuditTarget target, RequirementResult result, string observation, IReadOnlyDictionary<string, string> knownEvidenceIds) =>
        new(target.ComplianceAssessmentRefId, result.RequirementRef, result.Result, observation, result.EvidenceRefs)
        {
            Requirement = result.Key,
            KnownEvidenceIds = knownEvidenceIds,
        };

    private static RowsPlan PlanRows(
        AuditTarget target,
        CurrentAudit? audit,
        RequirementResult result,
        Dictionary<string, string> hashes,
        Dictionary<string, string> known,
        HashSet<string> documents,
        string runId)
    {
        var fingerprint = RowState.Fingerprint(result.Result, result.CheckOutcomes, result.EvidenceRefs.Select(id => $"{id}={hashes[id]}"));
        var knownIds = result.EvidenceRefs.Where(known.ContainsKey).Distinct().ToDictionary(id => id, id => known[id], StringComparer.Ordinal);
        var update = Update(target, result, RowState.Mark(result.Observation, runId, fingerprint), knownIds);
        var inScope = audit is null ? null : RowsInScope(audit, result.Key);
        if (inScope is null)
        {
            return new RowsPlan(target.ComplianceAssessmentRefId, update, Changed: 1, Unchanged: 0, Drift: []);
        }

        var desiredIds = result.EvidenceRefs.Select(id => known.GetValueOrDefault(id)).ToArray();
        var compared = inScope
            .Select(row => (Row: row, Kept: row.EvidenceIds.Where(documents.Contains).ToArray()))
            .Select(r => (r.Row, r.Kept, Change: Compare(r.Row, result.Result, [.. desiredIds, .. r.Kept], fingerprint)))
            .ToArray();
        var written = compared.Where(c => c.Change != RowChange.None).ToArray();
        var drift = written
            .Where(c => c.Change == RowChange.Drift)
            .Select(c => new Problem(
                "publish.drift_corrected",
                $"{target.ComplianceAssessmentRefId} {c.Row.Urn}: result was {Names.Of(c.Row.Result)}, now {Names.Of(result.Result)}; "
                + $"evidence was {c.Row.EvidenceIds.Count} item(s), now {result.EvidenceRefs.Count}. Someone changed this computed row in CISO Assistant; it was restored."))
            .ToArray();
        return new RowsPlan(
            target.ComplianceAssessmentRefId,
            written.Length == 0 ? null : update with
            {
                RowIds = [.. written.Select(c => c.Row.Id)],
                KeptEvidenceIds = written.Where(c => c.Kept.Length > 0).ToDictionary(c => c.Row.Id, c => (IReadOnlyList<string>)c.Kept, StringComparer.Ordinal),
            },
            written.Length,
            compared.Length - written.Length,
            drift);
    }

    /// <summary>The audit's rows for the requirement within its selected groups, or null when the publisher must find them (none known, or ambiguous).</summary>
    private static IReadOnlyList<CurrentRow>? RowsInScope(CurrentAudit audit, RequirementKey key)
    {
        var candidates = audit.Rows.Where(r => key.Kind == RequirementKeyKind.Urn ? r.Urn == key.Value : r.RefId == key.Value).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var remote = candidates.Select(r => new RemoteRequirementAssessment(r.Id, r.Urn, r.RefId, r.Groups)).ToArray();
        return RequirementRows.InScope(key, remote, audit.SelectedGroups).Match<IReadOnlyList<CurrentRow>?>(
            inScope => [.. candidates.Where(c => inScope.Any(r => r.Id == c.Id))],
            _ => null);
    }

    private static RowChange Compare(CurrentRow row, ComplianceResult result, IReadOnlyList<string?> desiredEvidenceIds, string fingerprint)
    {
        var holds = row.Result == result
            && desiredEvidenceIds.All(id => id is not null)
            && row.EvidenceIds.ToHashSet(StringComparer.Ordinal).SetEquals(desiredEvidenceIds.OfType<string>());
        return RowState.ReadMark(row.Observation)?.Fingerprint != fingerprint ? RowChange.Changed
            : holds ? RowChange.None
            : RowChange.Drift;
    }

    private static AuditChanges Tally(string audit, IReadOnlyList<RowsPlan> rows)
    {
        var mine = rows.Where(r => r.Audit == audit).ToArray();
        return new AuditChanges(audit, mine.Sum(r => r.Changed), mine.Sum(r => r.Unchanged), mine.Sum(r => r.Drift.Count));
    }

    private enum RowChange
    {
        None,
        Changed,
        Drift,
    }

    private sealed record RowsPlan(string Audit, AuditUpdate.UpdateRequirementAssessment? Update, int Changed, int Unchanged, IReadOnlyList<Problem> Drift);
}
