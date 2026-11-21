using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static RunRecord Summarise(
        string collector, DateTimeOffset startedAt, DateTimeOffset endedAt, CollectorOutcome outcome,
        IReadOnlyList<CheckResult> checks, IReadOnlyList<Problem> problems, PublishReport? publish)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var passed = checks.Count(c => c.Outcome == CheckOutcome.Pass);
        var failed = checks.Count(c => c.Outcome == CheckOutcome.Fail);
        return new RunRecord(
            collector, startedAt, endedAt, outcome, passed, failed, checks.Count - passed - failed, problems,
            publish is null ? null : new PublishCounts(publish.Planned, publish.Sent, publish.Skipped, publish.Failed));
    }

    public static string ToJson(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(Document.From(record), Options);
    }

    public static Result<RunRecord> Parse(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<Document>(json, Options) is { } document
                ? Result.Ok(document.ToRecord())
                : Invalid(source, "the file holds no run record.");
        }
        catch (JsonException ex)
        {
            return Invalid(source, ex.Message);
        }
    }

    private static Result<RunRecord> Invalid(string source, string why) =>
        Result.Fail<RunRecord>(new Problem("status.record_invalid", $"Run record {source} cannot be read: {why}"));

    // DTOs exist only for System.Text.Json; they never leave this class.
    private sealed record Document(
        string Collector,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        CollectorOutcome Outcome,
        int ChecksPassed,
        int ChecksFailed,
        int ChecksOther,
        IReadOnlyList<ProblemDocument> Problems,
        PublishCounts? Publish)
    {
        public static Document From(RunRecord r) => new(
            r.Collector, r.StartedAt, r.EndedAt, r.Outcome, r.ChecksPassed, r.ChecksFailed, r.ChecksOther,
            [.. r.Problems.Select(p => new ProblemDocument(p.Code, p.Message))], r.Publish);

        public RunRecord ToRecord() => new(
            Collector, StartedAt, EndedAt, Outcome, ChecksPassed, ChecksFailed, ChecksOther, [.. Problems.Select(p => new Problem(p.Code, p.Message))], Publish);
    }

    private sealed record ProblemDocument(string Code, string Message);
}
