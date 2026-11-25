using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Sprs;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>
/// s17-05: reads a methodology file from <c>frameworks/sprs/</c>: its name, version, starting score, and the points each requirement takes
/// off (5, 3 or 1; <c>none</c> for the system security plan). Every problem names the file and line.
/// </summary>
public static class SprsMethodologyReader
{
    private static readonly string[] TopLevelKeys = ["methodology", "version", "published", "start", "requirements"];
    private static readonly string[] RequirementKeys = ["ref", "points", "partial", "not_applicable_when", "note"];
    private static readonly string[] PartialKeys = ["points", "when"];
    private static readonly int[] Allowed = [5, 3, 1];

    public static Result<SprsMethodology> Parse(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);
        var at = new Locator(source);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<SprsMethodology>(at.Problem(ex.Start, "sprs.yaml_invalid", $"not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
        {
            return Result.Fail<SprsMethodology>(new Problem("sprs.document_not_a_map", $"{source}: the methodology file must be a map of keys."));
        }

        var problems = new List<Problem>(UnknownKeys(root, TopLevelKeys, at));
        var name = Text(root, "methodology", at, problems);
        var version = Text(root, "version", at, problems);
        Text(root, "published", at, problems);
        var start = Number(root, "start", at, problems);
        var weights = Child(root, "requirements") is YamlSequenceNode list
            ? list.Select(item => Weight(item, at, problems)).OfType<SprsWeight>().ToList()
            : Missing<List<SprsWeight>>(root, "requirements", at, problems) ?? [];
        foreach (var repeated in weights.GroupBy(w => w.Requirement, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add(new Problem("sprs.ref_repeated", $"{source}: requirement '{repeated.Key}' is listed more than once."));
        }

        return problems.Count > 0
            ? Result.Fail<SprsMethodology>(problems)
            : Result.Ok(new SprsMethodology(name!, version!, start!.Value, weights));
    }

    private static SprsWeight? Weight(YamlNode item, Locator at, List<Problem> problems)
    {
        if (item is not YamlMappingNode map)
        {
            problems.Add(at.Problem(item.Start, "sprs.requirement_not_a_map", "each requirement must be a map with 'ref' and 'points'."));
            return null;
        }

        problems.AddRange(UnknownKeys(map, RequirementKeys, at));
        var refId = Text(map, "ref", at, problems);
        var points = Child(map, "points") is YamlScalarNode { Value: "none" } ? null : Points(map, "points", at, problems);
        var partial = Child(map, "partial") switch
        {
            null => null,
            YamlMappingNode node => Partial(node, at, problems),
            var other => Invalid<SprsPartial>(other, "sprs.partial_invalid", "'partial' must be {points, when}.", at, problems),
        };
        var notApplicable = Child(map, "not_applicable_when") is null ? null : Text(map, "not_applicable_when", at, problems);
        var note = Child(map, "note") is null ? null : Text(map, "note", at, problems);
        return refId is null ? null : new SprsWeight(refId, points) { Partial = partial, NotApplicableWhen = notApplicable, Note = note };
    }

    private static SprsPartial? Partial(YamlMappingNode map, Locator at, List<Problem> problems)
    {
        problems.AddRange(UnknownKeys(map, PartialKeys, at));
        var points = Points(map, "points", at, problems);
        var when = Text(map, "when", at, problems);
        return points is null || when is null ? null : new SprsPartial(points.Value, when);
    }

    private static int? Points(YamlMappingNode map, string key, Locator at, List<Problem> problems)
    {
        var points = Number(map, key, at, problems);
        if (points is { } value && !Allowed.Contains(value))
        {
            problems.Add(at.Problem(Child(map, key)!.Start, "sprs.points_invalid", $"'{key}' must be 5, 3 or 1 (or none for the system security plan), not {value}."));
            return null;
        }

        return points;
    }

    private static int? Number(YamlMappingNode map, string key, Locator at, List<Problem> problems) => Child(map, key) switch
    {
        null => Missing<int?>(map, key, at, problems),
        YamlScalarNode { Value: { } text } when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) => value,
        var other => Invalid<int?>(other, "sprs.value_invalid", $"'{key}' must be a whole number.", at, problems),
    };

    private static string? Text(YamlMappingNode map, string key, Locator at, List<Problem> problems) => Child(map, key) switch
    {
        null => Missing<string>(map, key, at, problems),
        YamlScalarNode { Value.Length: > 0 } scalar => scalar.Value,
        var other => Invalid<string>(other, "sprs.value_invalid", $"'{key}' must be a non-empty text value.", at, problems),
    };

    private static T Missing<T>(YamlNode map, string key, Locator at, List<Problem> problems)
    {
        problems.Add(at.Problem(map.Start, "sprs.key_missing", $"'{key}' is missing."));
        return default!;
    }

    private static T? Invalid<T>(YamlNode node, string code, string message, Locator at, List<Problem> problems)
    {
        problems.Add(at.Problem(node.Start, code, message));
        return default;
    }

    private static IEnumerable<Problem> UnknownKeys(YamlMappingNode map, string[] allowed, Locator at) =>
        map.Children.Keys
            .Where(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal))
            .Select(key => at.Problem(key.Start, "sprs.key_unknown", $"unknown key '{key}'; expected one of {string.Join(", ", allowed)}."));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private sealed record Locator(string Source)
    {
        public Problem Problem(Mark mark, string code, string message) =>
            new(code, $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}");
    }
}
