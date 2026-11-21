using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Checks;

/// <summary>What a collector reports for one check. Framework-neutral.</summary>
public sealed record CheckResult
{
    public const int MaxObservationLength = 2000;

    private CheckResult(CheckId checkId, CheckOutcome outcome, string observation, IReadOnlyList<string> evidenceRefs, DateTimeOffset collectedAt, string source)
    {
        CheckId = checkId;
        Outcome = outcome;
        Observation = observation;
        EvidenceRefs = evidenceRefs;
        CollectedAt = collectedAt;
        Source = source;
    }

    public CheckId CheckId { get; }

    public CheckOutcome Outcome { get; }

    /// <summary>Short plain-text description of what was found.</summary>
    public string Observation { get; }

    /// <summary>IDs of the <see cref="Evidence"/> items that back this result.</summary>
    public IReadOnlyList<string> EvidenceRefs { get; }

    public DateTimeOffset CollectedAt { get; }

    /// <summary>Name of the collector that produced the result.</summary>
    public string Source { get; }

    public static Result<CheckResult> Create(
        CheckId checkId,
        CheckOutcome outcome,
        string observation,
        IEnumerable<string> evidenceRefs,
        DateTimeOffset collectedAt,
        string source)
    {
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        ArgumentNullException.ThrowIfNull(source);

        var refs = evidenceRefs.ToArray();
        var problems = new List<Problem>();
        if (observation.Length > MaxObservationLength)
        {
            problems.Add(new Problem("check_result.observation_too_long", $"Observation for {checkId} is {observation.Length} characters; the limit is {MaxObservationLength}."));
        }

        if (refs.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add(new Problem("check_result.evidence_ref_empty", $"Evidence references for {checkId} must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            problems.Add(new Problem("check_result.source_empty", $"Source for {checkId} must name the collector."));
        }

        return problems.Count == 0
            ? Result.Ok(new CheckResult(checkId, outcome, observation, refs, collectedAt, source))
            : Result.Fail<CheckResult>(problems);
    }
}
