using System.Globalization;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>scope/bia/&lt;bia&gt;.yaml</c> and the recovery objectives on systems (s15-04).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] BiaKeys =
    [
        "ref_id", "name", "description", "domain", "perimeter", "matrix", "version", "status", "authors", "reviewers", "due_date", "assessments",
    ];

    private static readonly string[] AssetAssessmentKeys = ["system", "dependencies", "controls", "recovery", "observation", "thresholds"];
    private static readonly string[] RecoveryFlagKeys = ["documented", "tested", "targets_met"];
    private static readonly string[] ThresholdKeys = ["after", "impact", "qualifications", "justification"];

    /// <summary>CISO Assistant's recovery objectives.</summary>
    private static readonly string[] RecoveryObjectives = ["rto", "rpo", "mtd"];

    private static ScopeBia? ReadBia(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, BiaKeys);
        var refId = file.Required(root, "ref_id");
        var name = file.Required(root, "name");
        var domain = file.Ref(root, "domain", required: true);
        var matrix = file.Ref(root, "matrix", required: true);
        var status = file.OneOf(root, "status", RiskStatuses, required: false);
        var assessments = file.Items(root, "assessments").Select(a => ReadAssetAssessment(a, file)).OfType<ScopeAssetAssessment>().ToArray();
        return refId is null || name is null || domain is null || matrix is null
            ? null
            : new ScopeBia(
                refId, name, file.Text(root, "description"), domain, file.Ref(root, "perimeter"), matrix, file.Text(root, "version"), status,
                file.Emails(root, "authors"), file.Emails(root, "reviewers"), file.Text(root, "due_date"), assessments, file.At(root));
    }

    private static ScopeAssetAssessment? ReadAssetAssessment(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, AssetAssessmentKeys);
        var system = file.Ref(item, "system", required: true);
        var recovery = file.Map(item, "recovery");
        if (recovery is not null)
        {
            file.Keys(recovery, RecoveryFlagKeys);
        }

        bool Flag(string key) => recovery is not null && file.OneOf(recovery, key, Booleans, required: false) == "true";
        return system is null
            ? null
            : new ScopeAssetAssessment(
                system, file.Refs(item, "dependencies"), file.Refs(item, "controls"), Flag("documented"), Flag("tested"), Flag("targets_met"),
                file.Text(item, "observation"), [.. file.Items(item, "thresholds").Select(t => ReadThreshold(t, file)).OfType<ScopeThreshold>()], file.At(item));
    }

    private static ScopeThreshold? ReadThreshold(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ThresholdKeys);
        var after = ReadDuration(item, "after", file, required: true);
        var impact = file.Ref(item, "impact", required: true);
        return after is null || impact is null
            ? null
            : new ScopeThreshold(after.Value, impact, [.. file.Refs(item, "qualifications").Select(q => q.Value)], file.Text(item, "justification"), file.At(item));
    }

    private static Duration? ReadDuration(YamlMappingNode map, string key, FileReader file, bool required)
    {
        var text = required ? file.Required(map, key) : file.Text(map, key);
        if (text is null)
        {
            return null;
        }

        if (Duration.TryParse(text, out var duration))
        {
            return duration;
        }

        file.Problem("scope.duration_invalid", file.At(FileReader.Child(map, key)!), $"'{key}' is '{text}', which is not a duration.", "Write a whole number and a unit: 30m, 4h, 2d or 1w.");
        return null;
    }

    /// <summary>A system's security objectives, each 0 to 4 on CISO Assistant's scale.</summary>
    private static Dictionary<string, int> ReadObjectives(YamlMappingNode item, FileReader file)
    {
        var objectives = new Dictionary<string, int>(StringComparer.Ordinal);
        if (file.Map(item, "objectives") is not { } map)
        {
            return objectives;
        }

        foreach (var (key, value) in map.Children)
        {
            if (key is YamlScalarNode { Value: { } name } && value is YamlScalarNode { Value: { } text }
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level <= 4)
            {
                objectives[name] = level;
            }
            else
            {
                file.Invalid(value, $"security objective '{key}' must be a whole number from 0 to 4.");
            }
        }

        return objectives;
    }

    /// <summary>A system's recovery objectives (<c>rto</c>, <c>rpo</c>, <c>mtd</c>) as durations.</summary>
    private static Dictionary<string, Duration> ReadRecovery(YamlMappingNode item, FileReader file)
    {
        var recovery = new Dictionary<string, Duration>(StringComparer.Ordinal);
        if (file.Map(item, "recovery") is not { } map)
        {
            return recovery;
        }

        file.Keys(map, RecoveryObjectives);
        foreach (var key in RecoveryObjectives)
        {
            if (ReadDuration(map, key, file, required: false) is { } duration)
            {
                recovery[key] = duration;
            }
        }

        return recovery;
    }
}
