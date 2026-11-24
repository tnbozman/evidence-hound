using System.Globalization;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>The <c>ai</c> block on a system: its entry in the AI register (s11-12).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] AiKeys =
        ["tier", "screened_on", "use", "oversight", "oversight_note", "impact_assessment", "impact_assessed_on", "in_use_from", "last_tested_on", "retired_on"];

    private static readonly string[] AiTiers = ["low", "medium", "high", "prohibited"];
    private static readonly string[] AiUses = ["built", "customised", "third_party"];
    private static readonly string[] AiOversights = ["human_in_the_loop", "human_on_the_loop", "human_out_of_loop"];
    private static readonly string[] AiImpactAssessments = ["not_started", "in_progress", "approved", "not_required"];

    private static AiRegistration? ReadAi(YamlMappingNode system, string? name, FileReader file)
    {
        if (file.Map(system, "ai") is not { } map)
        {
            return null;
        }

        file.Keys(map, AiKeys);
        var tier = file.OneOf(map, "tier", AiTiers, required: true);
        var screened = file.Required(map, "screened_on") is null ? null : AiDate(map, "screened_on", file);
        var use = file.OneOf(map, "use", AiUses, required: true);
        var oversight = file.OneOf(map, "oversight", AiOversights, required: true);
        var note = file.Required(map, "oversight_note");
        var impact = file.OneOf(map, "impact_assessment", AiImpactAssessments, required: false);
        var entry = new
        {
            ImpactAssessedOn = AiDate(map, "impact_assessed_on", file),
            InUseFrom = AiDate(map, "in_use_from", file),
            LastTestedOn = AiDate(map, "last_tested_on", file),
            RetiredOn = AiDate(map, "retired_on", file),
        };
        var at = file.At(map);
        var who = name ?? "This system";
        if (entry.ImpactAssessedOn is not null && impact != "approved")
        {
            file.Problem(
                "ai.impact_date_without_approval",
                at,
                $"{who} has 'impact_assessed_on' but its impact assessment is {impact ?? "not recorded"}.",
                "Set 'impact_assessment: approved' once the assessment is approved, or remove the date.");
        }

        if (entry.RetiredOn is { } retired && entry.InUseFrom is { } from && retired < from)
        {
            file.Problem(
                "ai.retired_before_use",
                at,
                $"{who} is retired on {retired:yyyy-MM-dd}, before it was in use from {from:yyyy-MM-dd}.",
                "Check both dates.");
        }

        if (use == "third_party" && FileReader.Child(system, "solution") is null)
        {
            file.Problem(
                "ai.solution_missing",
                at,
                $"{who} is a third-party AI system with no supplier's solution.",
                "Add 'solution' with the supplier's solution ref_id from scope/third-parties/, so its supplier assessment and contract are checked.");
        }

        return tier is null || screened is null || use is null || oversight is null || note is null
            ? null
            : new AiRegistration(Enum.Parse<AiRiskTier>(tier, ignoreCase: true), screened.Value, Pascal<AiUse>(use), Pascal<AiOversight>(oversight), note, at)
            {
                ImpactAssessment = impact is null ? null : Pascal<AiImpactAssessment>(impact),
                ImpactAssessedOn = entry.ImpactAssessedOn,
                InUseFrom = entry.InUseFrom,
                LastTestedOn = entry.LastTestedOn,
                RetiredOn = entry.RetiredOn,
            };
    }

    /// <summary>A snake_case code as its enum value, such as <c>human_on_the_loop</c> as <c>HumanOnTheLoop</c>.</summary>
    private static T Pascal<T>(string code)
        where T : struct, Enum =>
        Enum.Parse<T>(code.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);

    private static DateOnly? AiDate(YamlMappingNode map, string key, FileReader file) =>
        Date(map, key, file) is { } text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
