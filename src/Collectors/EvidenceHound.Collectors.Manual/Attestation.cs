using System.Globalization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using static EvidenceHound.Collectors.Manual.ResultProblems;

namespace EvidenceHound.Collectors.Manual;

/// <summary>
/// An owner's statement about one dropped file, kept next to it as <c>&lt;file&gt;.attest.yaml</c>: who attested, when, the
/// outcome for each check, and a note.
/// </summary>
public sealed record Attestation(string Who, DateTimeOffset Date, IReadOnlyDictionary<string, CheckOutcome> Outcomes, string? Note)
{
    public const string Suffix = ".attest.yaml";

    /// <summary>Who proposed the outcomes the person attested, such as <c>agent</c>; null when the person entered them unaided.</summary>
    public string? ProposedBy { get; init; }

    /// <summary>The SHA-256 of the file as it was when attested; null in a sidecar written before proposals.</summary>
    public string? FileSha256 { get; init; }

    private static readonly string Invalid = "manual.attestation_invalid";

    private static readonly Dictionary<string, CheckOutcome> OutcomesByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pass"] = CheckOutcome.Pass,
        ["fail"] = CheckOutcome.Fail,
        ["partial"] = CheckOutcome.Partial,
        ["not_applicable"] = CheckOutcome.NotApplicable,
    };

    /// <summary>The outcomes an owner can attest, as written in a sidecar.</summary>
    public static IReadOnlyList<string> OutcomeNames { get; } = ["pass", "fail", "partial", "not_applicable"];

    /// <summary>The sidecar file name for <paramref name="fileName"/>.</summary>
    public static string SidecarFor(string fileName) => fileName + Suffix;

    /// <summary>Reads an attestable outcome by its sidecar name, for example <c>not_applicable</c>.</summary>
    public static bool TryParseOutcome(string text, out CheckOutcome outcome) => OutcomesByName.TryGetValue(text.Trim(), out outcome);

    public static Result<Attestation> Parse(string yaml, string source)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents is [var document, ..] ? document.RootNode as YamlMappingNode : null;
        }
        catch (YamlException e)
        {
            return Result.Fail<Attestation>(Problem(source, $"is not valid YAML: {e.Message}"));
        }

        if (root is null)
        {
            return Result.Fail<Attestation>(Problem(source, "must be a map with who, date, outcomes and an optional note."));
        }

        var who = Text(root, "who") is { Length: > 0 } name
            ? Result.Ok(name)
            : Result.Fail<string>(Problem(source, "'who' is missing: name the person attesting."));
        var date = DateTimeOffset.TryParse(Text(root, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)
            ? Result.Ok(when)
            : Result.Fail<DateTimeOffset>(Problem(source, "'date' is missing or is not a date such as 2026-11-20."));
        var outcomes = ParseOutcomes(root, source);
        var note = Text(root, "note") is { Length: > 0 } text ? text : null;

        Problem[] problems = [.. ProblemsOf(who), .. ProblemsOf(date), .. ProblemsOf(outcomes)];
        return problems.Length > 0
            ? Result.Fail<Attestation>(problems)
            : who.Bind(w => date.Bind(d => outcomes.Map(o => new Attestation(w, d, o, note))));
    }

    public string ToYaml()
    {
        var document = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["who"] = Who,
            ["date"] = Date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        if (Note is not null)
        {
            document["note"] = Note;
        }

        document["outcomes"] = Outcomes.ToDictionary(pair => pair.Key, pair => NameOf(pair.Value), StringComparer.Ordinal);
        return new SerializerBuilder().Build().Serialize(document);
    }

    /// <summary>An attestable outcome's sidecar name, for example <c>not_applicable</c>.</summary>
    public static string NameOf(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Pass => "pass",
        CheckOutcome.Fail => "fail",
        CheckOutcome.Partial => "partial",
        CheckOutcome.NotApplicable => "not_applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Only pass, fail, partial and not_applicable can be attested."),
    };

    private static Result<IReadOnlyDictionary<string, CheckOutcome>> ParseOutcomes(YamlMappingNode root, string source) =>
        root.Children.TryGetValue(new YamlScalarNode("outcomes"), out var node) && node is YamlMappingNode map
            ? Result.Combine(map.Children.Select(pair => pair is { Key: YamlScalarNode { Value: { } check }, Value: YamlScalarNode { Value: { } value } } && TryParseOutcome(value, out var outcome)
                    ? Result.Ok(KeyValuePair.Create(check, outcome))
                    : Result.Fail<KeyValuePair<string, CheckOutcome>>(Problem(source, $"outcome '{pair.Value}' for '{pair.Key}' must be one of {string.Join(", ", OutcomeNames)}."))))
                .Map(pairs => (IReadOnlyDictionary<string, CheckOutcome>)pairs.ToDictionary(StringComparer.Ordinal))
            : Result.Ok<IReadOnlyDictionary<string, CheckOutcome>>(new Dictionary<string, CheckOutcome>(StringComparer.Ordinal));

    private static string? Text(YamlMappingNode root, string key) =>
        root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } value } ? value.Trim() : null;

    private static Problem Problem(string source, string message) => new(Invalid, $"{source}: {message}");
}
