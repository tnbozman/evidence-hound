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
    private static readonly string[] RequirementKeys = ["ref", "urn", "rows", "rule", "threshold", "checks"];

    public static Result<LoadedMapping> Parse(string yaml, string source, IReadOnlySet<CheckId>? knownChecks = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);

        var at = new Locator(source);
        return LoadRoot(yaml, at)
            .Bind(root => ParseMapping(root, at))
            .Map(parsed => new LoadedMapping(parsed.Mapping, UnknownCheckWarnings(parsed.CheckNodes, knownChecks, at))
            {
                Source = source,
                Lines = parsed.Lines,
            });
    }

    private static Result<YamlMappingNode> LoadRoot(string yaml, Locator at)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<YamlMappingNode>(at.Problem(ex.Start, "mapping.yaml_invalid", $"not valid YAML: {ex.Message}"));
        }

        return stream.Documents.FirstOrDefault()?.RootNode switch
        {
            null or YamlScalarNode { Value: null or "" } => Result.Fail<YamlMappingNode>(at.FileProblem("mapping.document_empty", "the mapping file is empty.")),
            YamlMappingNode root => Result.Ok(root),
            var other => Result.Fail<YamlMappingNode>(at.Problem(other.Start, "mapping.document_not_a_map", "the mapping file must be a map of keys.")),
        };
    }

    private static Result<ParsedMapping> ParseMapping(YamlMappingNode root, Locator at)
    {
        var urn = RequiredScalar(root, "framework_urn", at);
        var requirements = Child(root, "requirements") switch
        {
            null => Result.Fail<IReadOnlyList<ParsedRequirement>>(at.Problem(root.Start, "mapping.key_missing", "'requirements' is missing.")),
            YamlSequenceNode list => ParseRequirements(list, at),
            var other => Result.Fail<IReadOnlyList<ParsedRequirement>>(at.Problem(other.Start, "mapping.requirements_not_a_list", "'requirements' must be a list.")),
        };

        Problem[] problems = [.. UnknownKeys(root, TopLevelKeys, at), .. ProblemsOf(urn), .. ProblemsOf(requirements)];
        return problems.Length > 0
            ? Result.Fail<ParsedMapping>(problems)
            : urn.Bind(urnNode => requirements.Bind(parsed =>
                Located(FrameworkMapping.Create(urnNode.Value!, parsed.Select(r => r.Mapping)), urnNode.Start, at)
                    .Map(mapping => new ParsedMapping(mapping, [.. parsed.SelectMany(r => r.CheckNodes)], [.. parsed.Select(r => r.Line)]))));
    }

    private static Result<IReadOnlyList<ParsedRequirement>> ParseRequirements(YamlSequenceNode list, Locator at)
    {
        var parsed = Result.Combine(list.Children.Select(item => ParseRequirement(item, at)));
        Problem[] problems = [.. ProblemsOf(parsed), .. DuplicateRefs(list, at)];
        return problems.Length > 0 ? Result.Fail<IReadOnlyList<ParsedRequirement>>(problems) : parsed;
    }

    private static IEnumerable<Problem> DuplicateRefs(YamlSequenceNode list, Locator at)
    {
        var refs = list.Children
            .OfType<YamlMappingNode>()
            .Select(item => Child(item, "ref") ?? Child(item, "urn"))
            .OfType<YamlScalarNode>()
            .Where(node => !string.IsNullOrEmpty(node.Value))
            .ToArray();
        return refs
            .Select((node, index) => (Node: node, First: refs.Take(index).FirstOrDefault(earlier => earlier.Value == node.Value)))
            .Where(pair => pair.First is not null)
            .Select(pair => at.Problem(
                pair.Node.Start,
                "mapping.duplicate_requirement_ref",
                $"requirement '{pair.Node.Value}' is already used at line {pair.First!.Start.Line.ToString(CultureInfo.InvariantCulture)}."));
    }

    private static Result<ParsedRequirement> ParseRequirement(YamlNode item, Locator at)
    {
        if (item is not YamlMappingNode map)
        {
            return Result.Fail<ParsedRequirement>(at.Problem(item.Start, "mapping.requirement_not_a_map", "each requirement must be a map with ref, rule and checks."));
        }

        var key = ParseKey(map, at);
        var rule = ParseRule(map, at);
        var checks = ParseChecks(map, at);
        Problem[] problems = [.. UnknownKeys(map, RequirementKeys, at), .. ProblemsOf(key), .. ProblemsOf(rule), .. ProblemsOf(checks)];
        return problems.Length > 0
            ? Result.Fail<ParsedRequirement>(problems)
            : key.Bind(parsedKey => rule.Bind(parsedRule => checks.Bind(parsedChecks =>
                Located(RequirementMapping.Create(parsedKey, parsedChecks.Select(c => c.Id), parsedRule), map.Start, at)
                    .Map(mapping => new ParsedRequirement(mapping, [.. parsedChecks], (int)map.Start.Line)))));
    }

    /// <summary>Exactly one of <c>ref</c> and <c>urn</c>; <c>rows: all</c> only with a ref.</summary>
    private static Result<RequirementKey> ParseKey(YamlMappingNode map, Locator at)
    {
        var rows = Child(map, "rows");
        var hasRef = Child(map, "ref") is not null;
        var hasUrn = Child(map, "urn") is not null;
        if (hasRef == hasUrn)
        {
            return Result.Fail<RequirementKey>(at.Problem(map.Start, "mapping.requirement_name_invalid", "each requirement needs exactly one of 'ref' and 'urn'."));
        }

        if (rows is not null && (hasUrn || rows is not YamlScalarNode { Value: "all" }))
        {
            return Result.Fail<RequirementKey>(at.Problem(rows.Start, "mapping.rows_invalid", "'rows' can only be 'all', and only with 'ref': a urn names one row."));
        }

        return hasUrn
            ? RequiredScalar(map, "urn", at).Map(node => RequirementKey.Urn(node.Value!))
            : RequiredScalar(map, "ref", at).Map(node => RequirementKey.Ref(node.Value!, allRows: rows is not null));
    }

    private static Result<MappingRule> ParseRule(YamlMappingNode map, Locator at)
    {
        var threshold = Child(map, "threshold");
        return RequiredScalar(map, "rule", at).Bind(rule => (rule.Value, threshold) switch
        {
            ("all_pass" or "any_pass", not null) =>
                Result.Fail<MappingRule>(at.Problem(threshold.Start, "mapping.threshold_unexpected", $"'threshold' only applies to rule 'threshold', not '{rule.Value}'.")),
            ("all_pass", _) => Result.Ok(MappingRule.AllPass),
            ("any_pass", _) => Result.Ok(MappingRule.AnyPass),
            ("threshold", null) =>
                Result.Fail<MappingRule>(at.Problem(rule.Start, "mapping.threshold_missing", "rule 'threshold' needs a 'threshold' count.")),
            ("threshold", YamlScalarNode { Value: var count }) when int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) =>
                Result.Ok(MappingRule.Threshold(n)),
            ("threshold", _) =>
                Result.Fail<MappingRule>(at.Problem(threshold!.Start, "mapping.threshold_invalid", "'threshold' must be a whole number.")),
            _ => Result.Fail<MappingRule>(at.Problem(rule.Start, "mapping.rule_unknown", $"unknown rule '{rule.Value}'; use all_pass, any_pass or threshold.")),
        });
    }

    private static Result<IReadOnlyList<ParsedCheck>> ParseChecks(YamlMappingNode map, Locator at) => Child(map, "checks") switch
    {
        null => Result.Fail<IReadOnlyList<ParsedCheck>>(at.Problem(map.Start, "mapping.key_missing", "'checks' is missing.")),
        YamlSequenceNode list => Result.Combine(list.Children.Select(node =>
            Located(CheckId.Create((node as YamlScalarNode)?.Value ?? ""), node.Start, at).Map(id => new ParsedCheck(id, node)))),
        var other => Result.Fail<IReadOnlyList<ParsedCheck>>(at.Problem(other.Start, "mapping.checks_not_a_list", "'checks' must be a list of check IDs.")),
    };

    private static Result<YamlScalarNode> RequiredScalar(YamlMappingNode map, string key, Locator at) => Child(map, key) switch
    {
        null => Result.Fail<YamlScalarNode>(at.Problem(map.Start, "mapping.key_missing", $"'{key}' is missing.")),
        YamlScalarNode { Value.Length: > 0 } scalar => Result.Ok(scalar),
        var other => Result.Fail<YamlScalarNode>(at.Problem(other.Start, "mapping.value_invalid", $"'{key}' must be a non-empty text value.")),
    };

    private static IReadOnlyList<string> UnknownCheckWarnings(IEnumerable<ParsedCheck> checks, IReadOnlySet<CheckId>? knownChecks, Locator at) =>
        knownChecks is null
            ? []
            : [.. checks.Where(check => !knownChecks.Contains(check.Id)).Select(check => at.Message(check.Node.Start, $"check '{check.Id}' is not a known check."))];

    private static IEnumerable<Problem> UnknownKeys(YamlMappingNode map, string[] allowed, Locator at) =>
        map.Children.Keys
            .Where(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal))
            .Select(key => at.Problem(key.Start, "mapping.key_unknown", $"unknown key '{key}'; expected one of {string.Join(", ", allowed)}."));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static IReadOnlyList<Problem> ProblemsOf<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems);

    private static Result<T> Located<T>(Result<T> result, Mark mark, Locator at)
        where T : notnull =>
        result.Match(Result.Ok, problems => Result.Fail<T>(problems.Select(p => at.Problem(mark, p.Code, p.Message))));

    private sealed record ParsedCheck(CheckId Id, YamlNode Node);

    private sealed record ParsedRequirement(RequirementMapping Mapping, IReadOnlyList<ParsedCheck> CheckNodes, int Line);

    private sealed record ParsedMapping(FrameworkMapping Mapping, IReadOnlyList<ParsedCheck> CheckNodes, IReadOnlyList<int> Lines);

    private sealed record Locator(string Source)
    {
        public string Message(Mark mark, string message) =>
            $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}";

        public Problem FileProblem(string code, string message) => new(code, $"{Source}: {message}");

        public Problem Problem(Mark mark, string code, string message) => new(code, Message(mark, message));
    }
}
