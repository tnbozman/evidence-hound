using System.Globalization;
using System.Text.Json;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli;

/// <summary>Parses a check results file: <c>{"checks": [{"check_id", "outcome", "observation", "evidence_refs", "collected_at", "source"}]}</c>.</summary>
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

    public static Result<IReadOnlyList<CheckResult>> Parse(string json, string source)
    {
        ChecksDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ChecksDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            var line = ((ex.LineNumber ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
            return Result.Fail<IReadOnlyList<CheckResult>>(new Problem("checks.json_invalid", $"{source} line {line}: {ex.Message}"));
        }

        return document is null
            ? Result.Fail<IReadOnlyList<CheckResult>>(new Problem("checks.json_invalid", $"{source}: the file holds no checks object."))
            : Result.Combine(document.Checks.Select((entry, index) => ToCheckResult(entry, $"{source} checks[{index.ToString(CultureInfo.InvariantCulture)}]")));
    }

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
    private sealed record ChecksDocument(IReadOnlyList<CheckDocument> Checks);

    private sealed record CheckDocument(
        string CheckId,
        string Outcome,
        string Observation,
        IReadOnlyList<string> EvidenceRefs,
        DateTimeOffset CollectedAt,
        string Source);
}
