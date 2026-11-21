using System.Globalization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Parses a framework mapping file (see <c>schemas/mapping.schema.json</c>). Every problem names the file and line.</summary>
public static class MappingLoader
{
    private static readonly string[] TopLevelKeys = ["framework_urn", "description", "requirements"];
    private static readonly string[] RequirementKeys = ["ref", "rule", "threshold", "checks"];

    public static Result<LoadedMapping> Parse(string yaml, string source, IReadOnlySet<CheckId>? knownChecks = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);

        var located = new Locator(source);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<LoadedMapping>(located.Problem(ex.Start, "mapping.yaml_invalid", $"not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is YamlScalarNode { Value: null or "" })
        {
            return Result.Fail<LoadedMapping>(new Problem("mapping.document_empty", $"{source}: the mapping file is empty."));
        }

        return stream.Documents[0].RootNode is YamlMappingNode root
            ? ParseRoot(root, located, knownChecks)
            : Result.Fail<LoadedMapping>(located.Problem(stream.Documents[0].RootNode.Start, "mapping.document_not_a_map", "the mapping file must be a map of keys."));
    }

    private static Result<LoadedMapping> ParseRoot(YamlMappingNode root, Locator located, IReadOnlySet<CheckId>? knownChecks)
    {
        var problems = new List<Problem>();
        problems.AddRange(UnknownKeys(root, TopLevelKeys, located));

        var urn = RequiredScalar(root, "framework_urn", located, problems);
        var requirementsNode = Child(root, "requirements");
        var requirements = new List<(RequirementMapping Mapping, YamlNode Node)>();
        switch (requirementsNode)
        {
            case null:
                problems.Add(located.Problem(root.Start, "mapping.key_missing", "'requirements' is missing."));
                break;
            case YamlSequenceNode list:
                requirements.AddRange(ParseRequirements(list, located, problems));
                break;
            default:
                problems.Add(located.Problem(requirementsNode.Start, "mapping.requirements_not_a_list", "'requirements' must be a list."));
                break;
        }

        if (problems.Count > 0 || urn is null)
        {
            return Result.Fail<LoadedMapping>(problems);
        }

        var warnings = knownChecks is null
            ? []
            : requirements
                .SelectMany(r => ((YamlSequenceNode)((YamlMappingNode)r.Node).Children[new YamlScalarNode("checks")]).Children)
                .Where(node => CheckId.Create(((YamlScalarNode)node).Value ?? "").Match(id => !knownChecks.Contains(id), _ => false))
                .Select(node => located.Message(node.Start, $"check '{((YamlScalarNode)node).Value}' is not a known check."))
                .ToArray();

        return FrameworkMapping.Create(urn.Value!, requirements.Select(r => r.Mapping))
            .Match(
                mapping => Result.Ok(new LoadedMapping(mapping, warnings)),
                mappingProblems => Result.Fail<LoadedMapping>(mappingProblems.Select(p => located.Problem(urn.Start, p.Code, p.Message))));
    }

    private static List<(RequirementMapping Mapping, YamlNode Node)> ParseRequirements(YamlSequenceNode list, Locator located, List<Problem> problems)
    {
        var firstLineOfRef = new Dictionary<string, Mark>(StringComparer.Ordinal);
        var parsed = new List<(RequirementMapping, YamlNode)>();
        foreach (var item in list.Children)
        {
            if (item is not YamlMappingNode requirement)
            {
                problems.Add(located.Problem(item.Start, "mapping.requirement_not_a_map", "each requirement must be a map with ref, rule and checks."));
                continue;
            }

            var itemProblems = new List<Problem>();
            itemProblems.AddRange(UnknownKeys(requirement, RequirementKeys, located));
            var reference = RequiredScalar(requirement, "ref", located, itemProblems);
            var rule = ParseRule(requirement, located, itemProblems);
            var checks = ParseChecks(requirement, located, itemProblems);

            if (reference?.Value is { } refValue)
            {
                if (firstLineOfRef.TryGetValue(refValue, out var first))
                {
                    itemProblems.Add(located.Problem(
                        reference.Start,
                        "mapping.duplicate_requirement_ref",
                        $"requirement ref '{refValue}' is already used at line {first.Line.ToString(CultureInfo.InvariantCulture)}."));
                }
                else
                {
                    firstLineOfRef[refValue] = reference.Start;
                }
            }

            if (itemProblems.Count > 0 || reference is null || rule is null || checks is null)
            {
                problems.AddRange(itemProblems);
                continue;
            }

            switch (RequirementMapping.Create(reference.Value!, checks, rule))
            {
                case Result<RequirementMapping>.Ok ok:
                    parsed.Add((ok.Value, requirement));
                    break;
                case Result<RequirementMapping>.Fail fail:
                    problems.AddRange(fail.Errors.Select(p => located.Problem(requirement.Start, p.Code, p.Message)));
                    break;
            }
        }

        return parsed;
    }

    private static MappingRule? ParseRule(YamlMappingNode requirement, Locator located, List<Problem> problems)
    {
        var rule = RequiredScalar(requirement, "rule", located, problems);
        var threshold = Child(requirement, "threshold");
        switch (rule?.Value)
        {
            case null:
                return null;
            case "all_pass" or "any_pass" when threshold is not null:
                problems.Add(located.Problem(threshold.Start, "mapping.threshold_unexpected", $"'threshold' only applies to rule 'threshold', not '{rule.Value}'."));
                return null;
            case "all_pass":
                return MappingRule.AllPass;
            case "any_pass":
                return MappingRule.AnyPass;
            case "threshold" when threshold is null:
                problems.Add(located.Problem(rule.Start, "mapping.threshold_missing", "rule 'threshold' needs a 'threshold' count."));
                return null;
            case "threshold" when threshold is YamlScalarNode { Value: var count } && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n):
                return MappingRule.Threshold(n);
            case "threshold":
                problems.Add(located.Problem(threshold!.Start, "mapping.threshold_invalid", "'threshold' must be a whole number."));
                return null;
            default:
                problems.Add(located.Problem(rule.Start, "mapping.rule_unknown", $"unknown rule '{rule.Value}'; use all_pass, any_pass or threshold."));
                return null;
        }
    }

    private static List<CheckId>? ParseChecks(YamlMappingNode requirement, Locator located, List<Problem> problems)
    {
        switch (Child(requirement, "checks"))
        {
            case null:
                problems.Add(located.Problem(requirement.Start, "mapping.key_missing", "'checks' is missing."));
                return null;
            case YamlSequenceNode list:
                var ids = new List<CheckId>();
                var before = problems.Count;
                foreach (var node in list.Children)
                {
                    var value = (node as YamlScalarNode)?.Value ?? "";
                    switch (CheckId.Create(value))
                    {
                        case Result<CheckId>.Ok ok:
                            ids.Add(ok.Value);
                            break;
                        case Result<CheckId>.Fail fail:
                            problems.AddRange(fail.Errors.Select(p => located.Problem(node.Start, p.Code, p.Message)));
                            break;
                    }
                }

                return problems.Count == before ? ids : null;
            case var other:
                problems.Add(located.Problem(other.Start, "mapping.checks_not_a_list", "'checks' must be a list of check IDs."));
                return null;
        }
    }

    private static YamlScalarNode? RequiredScalar(YamlMappingNode map, string key, Locator located, List<Problem> problems)
    {
        switch (Child(map, key))
        {
            case null:
                problems.Add(located.Problem(map.Start, "mapping.key_missing", $"'{key}' is missing."));
                return null;
            case YamlScalarNode { Value: { Length: > 0 } } scalar:
                return scalar;
            case var other:
                problems.Add(located.Problem(other.Start, "mapping.value_invalid", $"'{key}' must be a non-empty text value."));
                return null;
        }
    }

    private static IEnumerable<Problem> UnknownKeys(YamlMappingNode map, string[] allowed, Locator located) =>
        map.Children.Keys
            .Where(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal))
            .Select(key => located.Problem(key.Start, "mapping.key_unknown", $"unknown key '{key}'; expected one of {string.Join(", ", allowed)}."));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private sealed record Locator(string Source)
    {
        public string Message(Mark mark, string message) =>
            $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}";

        public Problem Problem(Mark mark, string code, string message) => new(code, Message(mark, message));
    }
}
