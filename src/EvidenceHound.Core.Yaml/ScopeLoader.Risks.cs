using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>scope/risks/&lt;study&gt;.yaml</c>: a risk study and its scenarios (s15-02).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] RiskStudyKeys = ["ref_id", "name", "description", "domain", "perimeter", "matrix", "status", "authors", "reviewers", "eta", "due_date", "scenarios"];
    private static readonly string[] RiskScenarioKeys =
    [
        "ref_id", "name", "description", "systems", "threats", "existing_controls", "planned_controls", "current", "residual", "inherent", "treatment", "justification",
    ];

    private static readonly string[] RatingKeys = ["probability", "impact"];
    private static readonly string[] RiskStatuses = ["planned", "in_progress", "in_review", "done", "deprecated"];
    private static readonly string[] Treatments = ["open", "mitigate", "accept", "avoid", "transfer"];

    private static ScopeRiskStudy? ReadRiskStudy(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, RiskStudyKeys);
        var refId = file.Required(root, "ref_id");
        var name = file.Required(root, "name");
        var domain = file.Ref(root, "domain", required: true);
        var matrix = file.Ref(root, "matrix", required: true);
        var status = file.OneOf(root, "status", RiskStatuses, required: false);
        var scenarios = file.Items(root, "scenarios").Select(s => ReadRiskScenario(s, file)).OfType<ScopeRiskScenario>().ToArray();
        return refId is null || name is null || domain is null || matrix is null
            ? null
            : new ScopeRiskStudy(
                refId, name, domain, file.Ref(root, "perimeter"), matrix, status, file.Emails(root, "authors"), file.Emails(root, "reviewers"),
                file.Text(root, "eta"), file.Text(root, "due_date"), file.Text(root, "description"), scenarios, file.At(root));
    }

    private static ScopeRiskScenario? ReadRiskScenario(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, RiskScenarioKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var treatment = file.OneOf(item, "treatment", Treatments, required: false) ?? "open";
        return refId is null || name is null
            ? null
            : new ScopeRiskScenario(
                refId, name, file.Text(item, "description"), file.Refs(item, "systems"), file.Refs(item, "threats"), file.Refs(item, "existing_controls"),
                file.Refs(item, "planned_controls"), Rating(item, "current", file) ?? RiskRating.NotRated, Rating(item, "residual", file) ?? RiskRating.NotRated,
                Rating(item, "inherent", file), treatment, file.Text(item, "justification"), file.At(item));
    }

    /// <summary>A probability and impact by level name; <c>--</c> is not rated and read as null.</summary>
    private static RiskRating? Rating(YamlMappingNode item, string key, FileReader file)
    {
        if (file.Map(item, key) is not { } map)
        {
            return null;
        }

        file.Keys(map, RatingKeys);
        return new RiskRating(Level(file.Text(map, "probability")), Level(file.Text(map, "impact"))) { At = file.At(map) };
    }

    private static string? Level(string? name) => name is null or RiskRating.NotRatedName ? null : name;
}
