namespace EvidenceHound.Core.Scope;

/// <summary>The result of screening an AI system (AI6 practice 3). A prohibited system is not used.</summary>
public enum AiRiskTier
{
    Low,
    Medium,
    High,
    Prohibited,
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

/// <summary>A system's entry in the AI register (s11-12): the <c>ai</c> block of a system in <c>scope/setup.yaml</c>, reviewed by pull request.</summary>
public sealed record AiRegistration(AiRiskTier Tier, DateOnly ScreenedOn, AiUse Use, AiOversight Oversight, string OversightNote, SourceLine At)
{
    public AiImpactAssessment? ImpactAssessment { get; init; }

    public DateOnly? ImpactAssessedOn { get; init; }

    /// <summary>The go-live date; null while the system is being considered.</summary>
    public DateOnly? InUseFrom { get; init; }

    public DateOnly? LastTestedOn { get; init; }

    public DateOnly? RetiredOn { get; init; }
}

/// <summary>How an AI register entry reaches CISO Assistant's Community Edition: filtering labels, and lines added to the asset's description.</summary>
public static class AiRegister
{
    /// <summary>The label every AI system carries; the register is CISO Assistant's asset list filtered by it.</summary>
    public const string SystemLabel = "ai-system";

    public static IEnumerable<string> Labels(AiRegistration? ai) =>
        ai is null
            ? []
            : [
                SystemLabel,
                $"ai-tier-{Code(ai.Tier)}",
                $"ai-use-{Code(ai.Use)}",
                $"ai-oversight-{Code(ai.Oversight).Replace("human-", "", StringComparison.Ordinal)}",
                .. ai.RetiredOn is null ? Array.Empty<string>() : ["ai-retired"],
            ];

    /// <summary>The system's description, then the register entry, so a reader sees it in CISO Assistant and in its asset export.</summary>
    public static string? Description(string? description, AiRegistration? ai)
    {
        if (ai is null)
        {
            return description;
        }

        string[] dates =
        [
            $"Screened {Day(ai.ScreenedOn)}",
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
        var entry = string.Join(
            "\n",
            $"AI register: {Words(ai.Tier)} tier, {Words(ai.Use)}, {Words(ai.Oversight)}.",
            $"Oversight: {ai.OversightNote}",
            $"{string.Join("; ", dates)}.");
        return description is null ? entry : $"{description}\n\n{entry}";
    }

    /// <summary>
    /// The register entry read back from what CISO Assistant holds (for <c>hound scope export</c>): the labels give the tier, use and oversight, and
    /// the lines <see cref="Description"/> added give the rest. Returns the description without those lines, and null for the entry when the
    /// system has no <c>ai-system</c> label or its lines are not as hound wrote them.
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
        var ai = lines.Length == 3 && lines[1].StartsWith("Oversight: ", StringComparison.Ordinal) && lines[2].EndsWith('.')
            ? Entry(labels, lines[1]["Oversight: ".Length..], lines[2][..^1].Split("; "), at)
            : null;
        return ai is null ? (description, null) : (start == 0 ? null : description[..start], ai);
    }

    private static AiRegistration? Entry(IReadOnlyCollection<string> labels, string note, string[] dates, SourceLine at)
    {
        var tier = FromLabel<AiRiskTier>(labels, "ai-tier-", "");
        var use = FromLabel<AiUse>(labels, "ai-use-", "");
        var oversight = FromLabel<AiOversight>(labels, "ai-oversight-", "human-");
        if (tier is null || use is null || oversight is null || dates.Length is < 4 or > 5 || DayAfter(dates[0], "Screened ") is not { } screened)
        {
            return null;
        }

        var impact = dates[1] == "impact assessment not recorded" ? null : FromWords<AiImpactAssessment>(dates[1], "impact assessment ");
        var assessedOn = DayAfter(dates[1], "impact assessment approved ");
        var inUse = DayAfter(dates[2], "in use from ");
        var tested = DayAfter(dates[3], "last tested ");
        var retired = dates.Length == 5 ? DayAfter(dates[4], "retired ") : null;
        var valid = (impact is not null || dates[1] == "impact assessment not recorded" || assessedOn is not null)
            && (inUse is not null || dates[2] == "not yet in use")
            && (tested is not null || dates[3] == "not tested")
            && (dates.Length == 4 || retired is not null)
            && (retired is not null) == labels.Contains("ai-retired");
        return valid
            ? new AiRegistration(tier.Value, screened, use.Value, oversight.Value, note, at)
            {
                ImpactAssessment = assessedOn is null ? impact : AiImpactAssessment.Approved,
                ImpactAssessedOn = assessedOn,
                InUseFrom = inUse,
                LastTestedOn = tested,
                RetiredOn = retired,
            }
            : null;
    }

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
        && DateOnly.TryParseExact(text[prefix.Length..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>An enum value as the scope files write it, with hyphens: <c>HumanOnTheLoop</c> as <c>human-on-the-loop</c>.</summary>
    public static string Code<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => char.IsUpper(c) && i > 0 ? $"-{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));

    private static string Words<T>(T value)
        where T : struct, Enum =>
        Code(value).Replace('-', ' ');

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
