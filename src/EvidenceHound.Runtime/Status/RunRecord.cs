using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Status;

/// <summary>Publish counts for a run record; null in a record means the run did not publish.</summary>
public sealed record PublishCounts(int Planned, int Sent, int Skipped, int Failed);

/// <summary>What one collector run did: when, how it ended, how its checks came out, and what was published.</summary>
public sealed record RunRecord(
    string Collector,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    CollectorOutcome Outcome,
    int ChecksPassed,
    int ChecksFailed,
    int ChecksOther,
    IReadOnlyList<Problem> Problems,
    PublishCounts? Publish)
{
    public bool Equals(RunRecord? other) =>
        other is not null && Collector == other.Collector && StartedAt == other.StartedAt && EndedAt == other.EndedAt && Outcome == other.Outcome
        && (ChecksPassed, ChecksFailed, ChecksOther) == (other.ChecksPassed, other.ChecksFailed, other.ChecksOther)
        && Problems.SequenceEqual(other.Problems) && Equals(Publish, other.Publish);

    public override int GetHashCode() => HashCode.Combine(Collector, StartedAt, Outcome);
}

/// <summary>Builds and serialises run records. Pure.</summary>
public static class RunRecords
{
    public static RunRecord Summarise(
        string collector, DateTimeOffset startedAt, DateTimeOffset endedAt, CollectorOutcome outcome,
        IReadOnlyList<CheckResult> checks, IReadOnlyList<Problem> problems, PublishReport? publish) =>
        throw new NotImplementedException();

    public static string ToJson(RunRecord record) => throw new NotImplementedException();

    public static Result<RunRecord> Parse(string json, string source) => throw new NotImplementedException();
}
