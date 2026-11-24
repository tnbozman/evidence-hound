using System.Globalization;
using System.Text.RegularExpressions;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The outcome of the National AI Centre's AI screening questions (s11-11): normal when every answer is no, elevated when any is yes (more
/// governance: an approved impact assessment before use, closer oversight and testing), and unacceptable when the system is not to be used.
/// </summary>
public enum AiScreening
{
    Normal,
    Elevated,
    Unacceptable,
}

/// <summary>Whether the organisation built the AI system, customised one, or uses a supplier's product (AI6's develop and deploy split).</summary>
public enum AiUse
{
    Built,
    Customised,
    ThirdParty,
}

/// <summary>How people oversee the AI system (AI6 practice 6).</summary>
public enum AiOversight
{
    HumanInTheLoop,
    HumanOnTheLoop,
    HumanOutOfLoop,
}

/// <summary>Where the AI system's impact assessment is (AI6 practice 2).</summary>
public enum AiImpactAssessment
{
    NotStarted,
    InProgress,
    Approved,
    NotRequired,
}

/// <summary>
/// A system's entry in the AI register (s11-12): the <c>ai</c> block of a system in <c>scope/setup.yaml</c>, reviewed by pull request. Its
/// fields follow the National AI Centre's AI register template (s11-11); see the trace table in <c>docs/ai-grc.md</c>.
/// </summary>
public sealed record AiRegistration(AiScreening Screening, DateOnly ScreenedOn, AiUse Use, AiOversight Oversight, string OversightNote, SourceLine At)
{
    /// <summary>The screening questions (1 to 7) answered yes; empty when every answer was no.</summary>
    public IReadOnlyList<int> ScreeningYes { get; init; } = [];

    /// <summary>When the system was first added to the register.</summary>
    public DateOnly? RegisteredOn { get; init; }

    public string? Version { get; init; }

    /// <summary>Why the system exists and what it aims to achieve.</summary>
    public string? Purpose { get; init; }

    public IReadOnlyList<string> IntendedUses { get; init; } = [];

    /// <summary>What the system cannot or should not do.</summary>
    public string? Limitations { get; init; }

    /// <summary>How the system might be misused, or "None identified."</summary>
    public string? Misuse { get; init; }

    /// <summary>What data it learns from or processes, with its type (text, structured, image, audio).</summary>
    public IReadOnlyList<string> DataSources { get; init; } = [];

    /// <summary>Who might be affected by the system.</summary>
    public IReadOnlyList<string> Stakeholders { get; init; } = [];

    public AiImpactAssessment? ImpactAssessment { get; init; }

    public DateOnly? ImpactAssessedOn { get; init; }

    /// <summary>The go-live date; null while the system is being considered or piloted.</summary>
    public DateOnly? InUseFrom { get; init; }

    public DateOnly? LastTestedOn { get; init; }

    public DateOnly? RetiredOn { get; init; }
}

/// <summary>How an AI register entry reaches CISO Assistant's Community Edition: filtering labels, and lines added to the asset's description.</summary>
public static partial class AiRegister
{
    /// <summary>The label every AI system carries; the register is CISO Assistant's asset list filtered by it.</summary>
    public const string SystemLabel = "ai-system";

    /// <summary>The register template's text columns, as the description writes them, in order.</summary>
    private static readonly (string Label, Func<AiRegistration, string?> Get, Func<AiRegistration, string, AiRegistration> Set)[] TextLines =
    [
        ("Version", a => a.Version, (a, v) => a with { Version = v }),
        ("Purpose", a => a.Purpose, (a, v) => a with { Purpose = v }),
        ("Intended uses", a => Joined(a.IntendedUses), (a, v) => a with { IntendedUses = Split(v) }),
        ("Limitations and prohibited use", a => a.Limitations, (a, v) => a with { Limitations = v }),
        ("Foreseeable misuse", a => a.Misuse, (a, v) => a with { Misuse = v }),
        ("Data sources and type", a => Joined(a.DataSources), (a, v) => a with { DataSources = Split(v) }),
        ("Stakeholders affected", a => Joined(a.Stakeholders), (a, v) => a with { Stakeholders = Split(v) }),
    ];

    public static IEnumerable<string> Labels(AiRegistration? ai) =>
        ai is null
            ? []
            : [
                SystemLabel,
                $"ai-screening-{Code(ai.Screening)}",
                $"ai-use-{Code(ai.Use)}",
                $"ai-oversight-{Code(ai.Oversight).Replace("human-", "", StringComparison.Ordinal)}",
                .. ai.RetiredOn is null ? Array.Empty<string>() : ["ai-retired"],
            ];

    /// <summary>
    /// The system's description, then the register entry, so a reader sees it in CISO Assistant and in its asset export: the screening,
    /// the template's text columns that are filled, the oversight, and the dates.
    /// </summary>
    public static string? Description(string? description, AiRegistration? ai)
    {
        if (ai is null)
        {
            return description;
        }

        string[] dates =
        [
            .. ai.RegisteredOn is { } registered ? [$"registered {Day(registered)}"] : Array.Empty<string>(),
            $"screened {Day(ai.ScreenedOn)}",
            ai.ImpactAssessment switch
            {
                null => "impact assessment not recorded",
                AiImpactAssessment.Approved => $"impact assessment approved{(ai.ImpactAssessedOn is { } on ? $" {Day(on)}" : "")}",
                { } status => $"impact assessment {Words(status)}",
            },
            ai.InUseFrom is { } from ? $"in use from {Day(from)}" : "not yet in use",
            ai.LastTestedOn is { } tested ? $"last tested {Day(tested)}" : "not tested",
            .. ai.RetiredOn is { } retired ? [$"retired {Day(retired)}"] : Array.Empty<string>(),
        ];
        var datesLine = string.Join("; ", dates);
        var entry = string.Join(
            "\n",
            [
                $"AI register: {Words(ai.Screening)} screening ({Answers(ai.ScreeningYes)}), {Words(ai.Use)}, {Words(ai.Oversight)}.",
                .. TextLines.Select(t => (t.Label, Value: t.Get(ai))).Where(t => t.Value is not null).Select(t => $"{t.Label}: {t.Value}"),
                $"Oversight: {ai.OversightNote}",
                $"{char.ToUpperInvariant(datesLine[0])}{datesLine[1..]}.",
            ]);
        return description is null ? entry : $"{description}\n\n{entry}";
    }

    /// <summary>
    /// The register entry read back from what CISO Assistant holds (for <c>hound scope export</c>): the labels give the screening outcome, use
    /// and oversight, and the lines <see cref="Description"/> added give the rest. Returns the description without those lines, and null for the
    /// entry when the system has no <c>ai-system</c> label or its lines are not as hound wrote them.
    /// </summary>
    public static (string? Description, AiRegistration? Ai) Read(string? description, IReadOnlyCollection<string> labels, SourceLine at)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (!labels.Contains(SystemLabel) || description is null)
        {
            return (description, null);
        }

        var start = description.StartsWith("AI register: ", StringComparison.Ordinal) ? 0 : description.LastIndexOf("\n\nAI register: ", StringComparison.Ordinal);
        var lines = start < 0 ? [] : description[(start == 0 ? 0 : start + 2)..].Split('\n');
        var ai = lines.Length >= 3 ? Entry(labels, lines, at) : null;
        return ai is null ? (description, null) : (start == 0 ? null : description[..start], ai);
    }

    /// <summary>An enum value as the scope files write it, with hyphens: <c>HumanOnTheLoop</c> as <c>human-on-the-loop</c>.</summary>
    public static string Code<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => char.IsUpper(c) && i > 0 ? $"-{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));

    private static AiRegistration? Entry(IReadOnlyCollection<string> labels, string[] lines, SourceLine at)
    {
        var screening = FromLabel<AiScreening>(labels, "ai-screening-", "");
        var use = FromLabel<AiUse>(labels, "ai-use-", "");
        var oversight = FromLabel<AiOversight>(labels, "ai-oversight-", "human-");
        var header = Header().Match(lines[0]);
        var note = lines[^2].StartsWith("Oversight: ", StringComparison.Ordinal) ? lines[^2]["Oversight: ".Length..] : null;
        var dates = lines[^1].EndsWith('.') ? Dates(lines[^1][..^1].Split("; ")) : null;
        if (screening is null || use is null || oversight is null || !header.Success || note is null || dates is null)
        {
            return null;
        }

        var ai = dates(new AiRegistration(screening.Value, default, use.Value, oversight.Value, note, at) { ScreeningYes = Questions(header.Groups["yes"].Value) });
        var expected = 0;
        foreach (var line in lines[1..^2])
        {
            var index = Array.FindIndex(TextLines, expected, t => line.StartsWith(t.Label + ": ", StringComparison.Ordinal));
            if (index < 0 || ai is null)
            {
                return null;
            }

            ai = TextLines[index].Set(ai, line[(TextLines[index].Label.Length + 2)..]);
            expected = index + 1;
        }

        return ai is not null && (ai.RetiredOn is not null) == labels.Contains("ai-retired") ? ai : null;
    }

    /// <summary>The dates line read back, as a function that sets them on an entry; null when it is not as <see cref="Description"/> wrote it.</summary>
    private static Func<AiRegistration, AiRegistration?>? Dates(string[] parts)
    {
        var registered = DayAfter(parts[0], "Registered ");
        var rest = registered is null ? parts : parts[1..];
        if (rest.Length is < 4 or > 5 || DayAfter(rest[0], registered is null ? "Screened " : "screened ") is not { } screened)
        {
            return null;
        }

        var notRecorded = rest[1] == "impact assessment not recorded";
        var impact = notRecorded ? null : FromWords<AiImpactAssessment>(rest[1], "impact assessment ");
        var assessedOn = DayAfter(rest[1], "impact assessment approved ");
        var inUse = DayAfter(rest[2], "in use from ");
        var tested = DayAfter(rest[3], "last tested ");
        var retired = rest.Length == 5 ? DayAfter(rest[4], "retired ") : null;
        var valid = (impact is not null || notRecorded || assessedOn is not null)
            && (inUse is not null || rest[2] == "not yet in use")
            && (tested is not null || rest[3] == "not tested")
            && (rest.Length == 4 || retired is not null);
        return valid
            ? ai => ai with
            {
                ScreenedOn = screened,
                RegisteredOn = registered,
                ImpactAssessment = assessedOn is null ? impact : AiImpactAssessment.Approved,
                ImpactAssessedOn = assessedOn,
                InUseFrom = inUse,
                LastTestedOn = tested,
                RetiredOn = retired,
            }
            : null;
    }

    private static string Answers(IReadOnlyList<int> yes) =>
        yes.Count switch
        {
            0 => "no to every question",
            1 => $"yes to question {yes[0]}",
            _ => $"yes to questions {string.Join(", ", yes)}",
        };

    private static int[] Questions(string yes) =>
        yes.Length == 0 ? [] : [.. yes.Split(", ").Select(q => int.Parse(q, CultureInfo.InvariantCulture))];

    private static string? Joined(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join("; ", values);

    private static string[] Split(string value) => value.Split("; ");

    private static T? FromLabel<T>(IReadOnlyCollection<string> labels, string prefix, string codePrefix)
        where T : struct, Enum =>
        labels.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => FromCode<T>(codePrefix + l[prefix.Length..])).FirstOrDefault(v => v is not null);

    private static T? FromWords<T>(string text, string prefix)
        where T : struct, Enum =>
        text.StartsWith(prefix, StringComparison.Ordinal) ? FromCode<T>(text[prefix.Length..].Replace(' ', '-')) : null;

    private static T? FromCode<T>(string code)
        where T : struct, Enum =>
        Enum.GetValues<T>().Where(v => Code(v) == code).Select(v => (T?)v).FirstOrDefault();

    private static DateOnly? DayAfter(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal)
        && DateOnly.TryParseExact(text[prefix.Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static string Words<T>(T value)
        where T : struct, Enum =>
        Code(value).Replace('-', ' ');

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^AI register: [a-z]+ screening \((?:no to every question|yes to questions? (?<yes>\d(?:, \d)*))\), [a-z ]+, human [a-z ]+\.$")]
    private static partial Regex Header();
}
