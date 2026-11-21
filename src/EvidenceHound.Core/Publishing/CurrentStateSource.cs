using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>The audits, evidence ref_ids and applied control ref_ids whose current state a plan needs.</summary>
public sealed record StateQuery(IReadOnlyList<string> AuditRefIds, IReadOnlyList<string> EvidenceRefIds, IReadOnlyList<string> ControlRefIds)
{
    /// <summary>Everything a plan of these inputs could touch: each target's audit, each evidence item and each configured control. Pure.</summary>
    public static StateQuery For(IReadOnlyList<TargetResults> targets, IReadOnlyList<Evidence> evidence, IReadOnlyList<ControlStatus> controlStatuses)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(controlStatuses);
        return new(
            [.. targets.Select(t => t.Target.ComplianceAssessmentRefId).Distinct(StringComparer.Ordinal)],
            [.. evidence.Select(e => e.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. controlStatuses.SelectMany(c => targets.Select(t => t.Target.AppliedControlRefIds.GetValueOrDefault(c.ControlKey))).OfType<string>().Distinct(StringComparer.Ordinal)]);
    }
}

/// <summary>Reads the current state a plan is compared with.</summary>
public interface ICurrentStateSource
{
    /// <summary>What is known. Anything that cannot be read is left out, so it is planned and sent, and a real failure shows when publishing.</summary>
    Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken);
}

/// <summary>Knows nothing, so every update is planned: for dry runs and transports that cannot read CISO Assistant.</summary>
public sealed class NoCurrentState : ICurrentStateSource
{
    public static NoCurrentState Instance { get; } = new();

    public Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken) => Task.FromResult(CurrentState.Empty);
}

/// <summary>Reads the current state from CISO Assistant.</summary>
public sealed class ClientStateSource(ICisoAssistantClient client) : ICurrentStateSource
{
    public async Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var audits = new List<CurrentAudit>();
        foreach (var refId in query.AuditRefIds)
        {
            var audit = await client.FindAuditAsync(refId, cancellationToken);
            var rows = await audit.BindAsync(_ => client.ListRequirementAssessmentsAsync(refId, cancellationToken));
            audits.AddRange(audit.Bind(a => rows.Map(all => new CurrentAudit(refId, a.SelectedGroups, all))).Match<CurrentAudit[]>(a => [a], _ => []));
        }

        var evidence = new List<CurrentEvidence>();
        foreach (var refId in query.EvidenceRefIds)
        {
            var found = await client.FindEvidenceAsync(refId, cancellationToken);
            evidence.AddRange(found.Match(all => all.Select(e => new CurrentEvidence(refId, e.Id, e.LatestSha256)), _ => []));
        }

        var controls = new List<CurrentControl>();
        foreach (var refId in query.ControlRefIds)
        {
            var status = await client.FindAppliedControlStatusAsync(refId, cancellationToken);
            controls.AddRange(status.Match<CurrentControl[]>(s => [new CurrentControl(refId, s)], _ => []));
        }

        return new CurrentState(audits, evidence, controls);
    }
}
