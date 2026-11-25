using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Publishing;

/// <summary>What a run did to the BIA recovery flags named by checks: the flags it set, and what it could not do.</summary>
public sealed record RecoveryReport(IReadOnlyList<RecoveryFlagUpdate> Set, IReadOnlyList<Problem> Problems)
{
    public static RecoveryReport None { get; } = new([], []);
}

/// <summary>Sets the BIA recovery flags named by checks after a run (s15-05).</summary>
public static class RecoveryPublisher
{
    /// <summary>
    /// Reads each BIA that names checks for a flag, sets each flag whose value changed, and links the evidence of its checks that CISO
    /// Assistant already holds (evidence a run published for some requirement); evidence it does not hold is named, and the flag still set.
    /// </summary>
    public static async Task<RecoveryReport> PublishAsync(ICisoAssistantClient client, IReadOnlyList<ScopeBia> bias, IReadOnlyList<CheckResult> latest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(bias);
        var named = bias.Where(b => b.Assessments.Any(a => a.Checks.Count > 0)).ToList();
        var problems = new List<Problem>();
        var held = new Dictionary<string, HeldRecovery>(StringComparer.Ordinal);
        foreach (var bia in named)
        {
            var read = await client.ListRecoveryAsync(bia.RefId, cancellationToken);
            read.Match(
                assessments =>
                {
                    foreach (var (system, assessment) in assessments)
                    {
                        held[BiaObjects.AssessmentKey(bia.RefId, system)] = assessment;
                    }

                    return Unit.Value;
                },
                p =>
                {
                    problems.AddRange(p);
                    return Unit.Value;
                });
        }

        var set = new List<RecoveryFlagUpdate>();
        foreach (var update in RecoveryFlags.Plan(named, latest, held))
        {
            var evidence = new List<string>(held.Values.First(h => h.Id == update.AssessmentId).EvidenceIds);
            foreach (var refId in update.EvidenceRefs)
            {
                var found = await client.FindEvidenceAsync(refId, cancellationToken);
                if (found is Result<IReadOnlyList<RemoteEvidence>>.Ok { Value: [var remote, ..] })
                {
                    evidence.Add(remote.Id);
                }
                else
                {
                    problems.Add(new Problem(
                        "recovery.evidence_not_published",
                        $"{update.AssessmentKey}: evidence {refId} for {update.Field} is not in CISO Assistant, so it is not linked. Map its check to a requirement so a run publishes it."));
                }
            }

            var sent = await client.UpdateRecoveryAsync(update.AssessmentId, update.Field, update.Value, [.. evidence.Distinct(StringComparer.Ordinal)], cancellationToken);
            sent.Match(
                _ =>
                {
                    set.Add(update);
                    return Unit.Value;
                },
                p =>
                {
                    problems.AddRange(p);
                    return Unit.Value;
                });
        }

        return new RecoveryReport(set, problems);
    }
}
