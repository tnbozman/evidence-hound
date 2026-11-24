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

    /// <summary>An enum value as the scope files write it, with hyphens: <c>HumanOnTheLoop</c> as <c>human-on-the-loop</c>.</summary>
    public static string Code<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => char.IsUpper(c) && i > 0 ? $"-{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));

    private static string Words<T>(T value)
        where T : struct, Enum =>
        Code(value).Replace('-', ' ');

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
