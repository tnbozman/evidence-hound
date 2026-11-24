using System.Globalization;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>The <c>ai</c> block on a system: its entry in the AI register (s11-12).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] AiKeys =
    [
        "screening", "screening_yes", "screened_on", "registered_on", "version", "use", "purpose", "intended_uses", "limitations", "misuse",
        "data_sources", "stakeholders", "oversight", "oversight_note", "impact_assessment", "impact_assessed_on", "in_use_from", "last_tested_on",
        "retired_on",
    ];

    /// <summary>The outcomes of the National AI Centre's AI screening questions (s11-11).</summary>
    private static readonly string[] AiScreenings = ["normal", "elevated", "unacceptable"];

    /// <summary>The number of National AI Centre screening questions.</summary>
    private static readonly int AiScreeningQuestions = 7;

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
        var screening = file.OneOf(map, "screening", AiScreenings, required: true);
        var yes = AiScreeningYes(map, file);
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
            RegisteredOn = AiDate(map, "registered_on", file),
        };
        var at = file.At(map);
        var who = name ?? "This system";
        if (yes is not null && ((screening == "normal" && yes.Length > 0) || (screening == "elevated" && yes.Length == 0)))
        {
            file.Problem(
                "ai.screening_disagrees",
                at,
                yes.Length > 0
                    ? $"{who} is screened {screening} but answered yes to question {string.Join(", ", yes)}."
                    : $"{who} is screened {screening} but answered no to every question.",
                "Any yes means elevated; no to every question means normal (unacceptable can follow any answers). See runbooks/ai-screening.md.");
        }

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

        return screening is null || yes is null || screened is null || use is null || oversight is null || note is null
            ? null
            : new AiRegistration(Pascal<AiScreening>(screening), screened.Value, Pascal<AiUse>(use), Pascal<AiOversight>(oversight), note, at)
            {
                ScreeningYes = yes,
                RegisteredOn = entry.RegisteredOn,
                Version = file.Text(map, "version"),
                Purpose = file.Text(map, "purpose"),
                IntendedUses = Texts(map, "intended_uses", file),
                Limitations = file.Text(map, "limitations"),
                Misuse = file.Text(map, "misuse"),
                DataSources = Texts(map, "data_sources", file),
                Stakeholders = Texts(map, "stakeholders", file),
                ImpactAssessment = impact is null ? null : Pascal<AiImpactAssessment>(impact),
                ImpactAssessedOn = entry.ImpactAssessedOn,
                InUseFrom = entry.InUseFrom,
                LastTestedOn = entry.LastTestedOn,
                RetiredOn = entry.RetiredOn,
            };
    }

    /// <summary>The screening questions answered yes, by number; required, and an empty list when every answer was no.</summary>
    private static int[]? AiScreeningYes(YamlMappingNode map, FileReader file)
    {
        if (FileReader.Child(map, "screening_yes") is null)
        {
            file.Required(map, "screening_yes");
            return null;
        }

        var answers = file.Refs(map, "screening_yes");
        var numbers = answers.Select(a => int.TryParse(a.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= AiScreeningQuestions ? n : (int?)null).ToArray();
        foreach (var bad in answers.Where((_, i) => numbers[i] is null))
        {
            file.Problem("scope.value_invalid", bad.At, $"'screening_yes' has '{bad.Value}'; expected question numbers 1 to {AiScreeningQuestions}.", "List the numbers of the screening questions answered yes, such as [1, 5], or [] for none.");
        }

        return numbers.Any(n => n is null) ? null : [.. numbers.OfType<int>().Distinct().Order()];
    }

    private static string[] Texts(YamlMappingNode map, string key, FileReader file) => [.. file.Refs(map, key).Select(r => r.Value)];

    /// <summary>A snake_case code as its enum value, such as <c>human_on_the_loop</c> as <c>HumanOnTheLoop</c>.</summary>
    private static T Pascal<T>(string code)
        where T : struct, Enum =>
        Enum.Parse<T>(code.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);

    private static DateOnly? AiDate(YamlMappingNode map, string key, FileReader file) =>
        Date(map, key, file) is { } text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
