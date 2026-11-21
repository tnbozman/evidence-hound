using System.Globalization;
using System.Text.Json;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli;

/// <summary>What one collection run produced: check results, the evidence they cite and observed control statuses.</summary>
internal sealed record RunResults(IReadOnlyList<CheckResult> Checks, IReadOnlyList<Evidence> Evidence, IReadOnlyList<ControlStatus> ControlStatuses);

/// <summary>
/// Parses a check results file: <c>{"checks": [{"check_id", "outcome", "observation", "evidence_refs", "collected_at", "source"}],
/// "evidence": [{"id", "file_name", "media_type", "sha256", "size_bytes", "collected_at"}], "control_statuses": [{"control", "status"}]}</c>.
/// <c>evidence</c> and <c>control_statuses</c> are optional.
/// </summary>
internal static class CheckResultsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Dictionary<string, CheckOutcome> Outcomes =
        Enum.GetValues<CheckOutcome>().ToDictionary(Names.Of, StringComparer.Ordinal);

    private static readonly Dictionary<string, ControlState> ControlStates = new(StringComparer.Ordinal)
    {
        ["to_do"] = ControlState.ToDo,
        ["in_progress"] = ControlState.InProgress,
        ["on_hold"] = ControlState.OnHold,
        ["active"] = ControlState.Active,
        ["deprecated"] = ControlState.Deprecated,
    };

    public static Result<IReadOnlyList<CheckResult>> Parse(string json, string source) =>
        ParseRun(json, source).Map(run => run.Checks);

    public static Result<RunResults> ParseRun(string json, string source)
    {
        ChecksDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ChecksDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            var line = ((ex.LineNumber ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
            return Result.Fail<RunResults>(new Problem("checks.json_invalid", $"{source} line {line}: {ex.Message}"));
        }

        if (document is null)
        {
            return Result.Fail<RunResults>(new Problem("checks.json_invalid", $"{source}: the file holds no checks object."));
        }

        var checks = Result.Combine(document.Checks.Select((entry, index) => ToCheckResult(entry, At(source, "checks", index))));
        var evidence = Result.Combine((document.Evidence ?? []).Select((entry, index) => ToEvidence(entry, At(source, "evidence", index))));
        var controls = Result.Combine((document.ControlStatuses ?? []).Select((entry, index) => ToControlStatus(entry, At(source, "control_statuses", index))));
        return checks.Bind(c => evidence.Bind(e => controls.Map(s => new RunResults(c, e, s))))
            .Match(Result.Ok, _ => Result.Fail<RunResults>([.. Problems(checks), .. Problems(evidence), .. Problems(controls)]));
    }

    public static async Task<Result<RunResults>> LoadRunAsync(FileInfo file, CancellationToken cancellationToken) =>
        file.Exists
            ? ParseRun(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.ToString())
            : Result.Fail<RunResults>(new Problem("checks.file_not_found", $"Check results file not found: {file}"));

    private static string At(string source, string list, int index) => $"{source} {list}[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static IReadOnlyList<Problem> Problems<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems);

    private static Result<Evidence> ToEvidence(EvidenceDocument entry, string at) =>
        Located(Evidence.Create(entry.Id, entry.FileName, entry.MediaType, entry.Sha256, entry.SizeBytes, entry.CollectedAt), at);

    private static Result<ControlStatus> ToControlStatus(ControlStatusDocument entry, string at) =>
        ControlStates.TryGetValue(entry.Status, out var state)
            ? Result.Ok(new ControlStatus(entry.Control, state))
            : Result.Fail<ControlStatus>(new Problem(
                "checks.control_status_unknown",
                $"{at}: unknown control status '{entry.Status}'; use {string.Join(", ", ControlStates.Keys)}."));

    private static Result<CheckResult> ToCheckResult(CheckDocument entry, string at) =>
        Located(CheckId.Create(entry.CheckId), at)
            .Bind(id => Outcomes.TryGetValue(entry.Outcome, out var outcome)
                ? Located(CheckResult.Create(id, outcome, entry.Observation, entry.EvidenceRefs, entry.CollectedAt, entry.Source), at)
                : Result.Fail<CheckResult>(new Problem(
                    "checks.outcome_unknown",
                    $"{at}: unknown outcome '{entry.Outcome}'; use {string.Join(", ", Outcomes.Keys)}.")));

    private static Result<T> Located<T>(Result<T> result, string at)
        where T : notnull =>
        result.Match(Result.Ok, problems => Result.Fail<T>(problems.Select(p => new Problem(p.Code, $"{at}: {p.Message}"))));

    // DTOs exist only for System.Text.Json; they never leave this class.
    private sealed record ChecksDocument(
        IReadOnlyList<CheckDocument> Checks,
        IReadOnlyList<EvidenceDocument>? Evidence = null,
        IReadOnlyList<ControlStatusDocument>? ControlStatuses = null);

    private sealed record EvidenceDocument(string Id, string FileName, string MediaType, string Sha256, long SizeBytes, DateTimeOffset CollectedAt);

    private sealed record ControlStatusDocument(string Control, string Status);

    private sealed record CheckDocument(
        string CheckId,
        string Outcome,
        string Observation,
        IReadOnlyList<string> EvidenceRefs,
        DateTimeOffset CollectedAt,
        string Source);
}
