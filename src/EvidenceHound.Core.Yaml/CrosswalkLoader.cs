using System.Globalization;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>
/// s12-08: parses a crosswalk file (see <c>schemas/crosswalk.schema.json</c>): the target framework's requirements, each with the source
/// requirements that answer it. Every problem names the file and line.
/// </summary>
public static class CrosswalkLoader
{
    private static readonly string[] TopLevelKeys = ["from", "to", "reviewed", "requirements"];
    private static readonly string[] ReviewKeys = ["by", "on"];
    private static readonly string[] TargetKeys = ["ref", "title", "answered_by"];
    private static readonly string[] LinkKeys = ["ref", "relationship"];

    private static readonly Dictionary<string, CrosswalkRelationship> Relationships = new(StringComparer.Ordinal)
    {
        ["equal"] = CrosswalkRelationship.Equal,
        ["superset"] = CrosswalkRelationship.Superset,
        ["subset"] = CrosswalkRelationship.Subset,
        ["intersect"] = CrosswalkRelationship.Intersect,
    };

    public static Result<CrosswalkDefinition> Parse(string yaml, string source)
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
            return Result.Fail<CrosswalkDefinition>(at.Problem(ex.Start, "crosswalk.yaml_invalid", $"not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
        {
            return Result.Fail<CrosswalkDefinition>(new Problem("crosswalk.document_not_a_map", $"{source}: the crosswalk file must be a map of keys."));
        }

        var problems = new List<Problem>(UnknownKeys(root, TopLevelKeys, at));
        var from = Urn(root, "from", at, problems);
        var to = Urn(root, "to", at, problems);
        var review = Child(root, "reviewed") is { } reviewed ? Review(reviewed, at, problems) : null;
        var targets = new List<CrosswalkTarget>();
        var links = new List<CrosswalkLink>();
        switch (Child(root, "requirements"))
        {
            case null:
                problems.Add(at.Problem(root.Start, "crosswalk.key_missing", "'requirements' is missing."));
                break;
            case YamlSequenceNode list:
                foreach (var item in list)
                {
                    Target(item, at, problems, targets, links);
                }

                break;
            case var other:
                problems.Add(at.Problem(other.Start, "crosswalk.requirements_not_a_list", "'requirements' must be a list."));
                break;
        }

        return problems.Count > 0
            ? Result.Fail<CrosswalkDefinition>(problems)
            : Result.Ok(new CrosswalkDefinition(from!, to!, targets, links, source) { Review = review });
    }

    private static void Target(YamlNode item, Locator at, List<Problem> problems, List<CrosswalkTarget> targets, List<CrosswalkLink> links)
    {
        if (item is not YamlMappingNode map)
        {
            problems.Add(at.Problem(item.Start, "crosswalk.requirement_not_a_map", "each requirement must be a map with 'ref' and 'title'."));
            return;
        }

        problems.AddRange(UnknownKeys(map, TargetKeys, at));
        var refId = Text(map, "ref", at, problems);
        var title = Text(map, "title", at, problems);
        if (refId is null || title is null)
        {
            return;
        }

        if (targets.Exists(t => t.RefId == refId))
        {
            problems.Add(at.Problem(map.Start, "crosswalk.ref_repeated", $"requirement '{refId}' is listed more than once."));
            return;
        }

        targets.Add(new CrosswalkTarget(refId, title));
        switch (Child(map, "answered_by"))
        {
            case null:
                break;
            case YamlSequenceNode list:
                links.AddRange(list.Select(link => Link(refId, link, at, problems)).OfType<CrosswalkLink>());
                break;
            case var other:
                problems.Add(at.Problem(other.Start, "crosswalk.answered_by_not_a_list", "'answered_by' must be a list of {ref, relationship}."));
                break;
        }
    }

    private static CrosswalkLink? Link(string target, YamlNode node, Locator at, List<Problem> problems)
    {
        if (node is not YamlMappingNode map)
        {
            problems.Add(at.Problem(node.Start, "crosswalk.link_not_a_map", "each 'answered_by' entry must be a map with 'ref' and 'relationship'."));
            return null;
        }

        problems.AddRange(UnknownKeys(map, LinkKeys, at));
        var refId = Text(map, "ref", at, problems);
        var relationship = Text(map, "relationship", at, problems);
        if (refId is null || relationship is null)
        {
            return null;
        }

        if (!Relationships.TryGetValue(relationship, out var parsed))
        {
            problems.Add(at.Problem(Child(map, "relationship")!.Start, "crosswalk.relationship_unknown", $"unknown relationship '{relationship}'; use equal, superset, subset or intersect."));
            return null;
        }

        return new CrosswalkLink(target, refId, parsed);
    }

    private static CrosswalkReview? Review(YamlNode node, Locator at, List<Problem> problems)
    {
        if (node is not YamlMappingNode map)
        {
            problems.Add(at.Problem(node.Start, "crosswalk.reviewed_invalid", "'reviewed' must be {by: <name or role>, on: YYYY-MM-DD}."));
            return null;
        }

        problems.AddRange(UnknownKeys(map, ReviewKeys, at));
        var by = Text(map, "by", at, problems);
        var on = Text(map, "on", at, problems);
        if (by is null || on is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            problems.Add(at.Problem(Child(map, "on")!.Start, "crosswalk.reviewed_invalid", $"'on' must be a date like 2026-11-20, not '{on}'."));
            return null;
        }

        return new CrosswalkReview(by, date);
    }

    private static string? Urn(YamlMappingNode map, string key, Locator at, List<Problem> problems)
    {
        var value = Text(map, key, at, problems);
        if (value is not null && !value.StartsWith("urn:", StringComparison.Ordinal))
        {
            problems.Add(at.Problem(Child(map, key)!.Start, "crosswalk.urn_invalid", $"'{key}' must be a framework URN starting with 'urn:', not '{value}'."));
            return null;
        }

        return value;
    }

    private static string? Text(YamlMappingNode map, string key, Locator at, List<Problem> problems)
    {
        switch (Child(map, key))
        {
            case null:
                problems.Add(at.Problem(map.Start, "crosswalk.key_missing", $"'{key}' is missing."));
                return null;
            case YamlScalarNode { Value.Length: > 0 } scalar:
                return scalar.Value;
            case var other:
                problems.Add(at.Problem(other.Start, "crosswalk.value_invalid", $"'{key}' must be a non-empty text value."));
                return null;
        }
    }

    private static IEnumerable<Problem> UnknownKeys(YamlMappingNode map, string[] allowed, Locator at) =>
        map.Children.Keys
            .Where(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal))
            .Select(key => at.Problem(key.Start, "crosswalk.key_unknown", $"unknown key '{key}'; expected one of {string.Join(", ", allowed)}."));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private sealed record Locator(string Source)
    {
        public Problem Problem(Mark mark, string code, string message) =>
            new(code, $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}");
    }
}
