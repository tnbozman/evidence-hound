using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Onboarding.Skills;

/// <summary>The steps a skill can guide: the onboarding steps, and explaining a change for review.</summary>
public enum SkillStep
{
    Supplier,
    System,
    Applicability,
    Profile,
    Mapping,
    Explain,
    Environment,
}

public enum SkillSource
{
    Default,
    Organisation,
}

/// <summary>
/// Guidance for the model at one or more onboarding steps: a Markdown body with frontmatter. Only text given to the model; the rules that
/// keep onboarding safe stay in code and no skill can change them.
/// </summary>
public sealed record Skill(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<SkillStep> AppliesTo,
    IReadOnlyList<string> Frameworks,
    string? Overrides,
    string Body,
    SkillSource Source,
    string Path,
    string Sha256);

/// <summary>Reads a skill file once into an immutable <see cref="Skill"/>, reporting every problem with the file's path. Pure.</summary>
public static partial class SkillParser
{
    public const int MaxBodyCharacters = 24_000;

    private static readonly string[] Keys = ["name", "description", "version", "applies_to", "frameworks", "overrides"];

    public static IReadOnlyList<string> Steps { get; } = ["supplier", "system", "applicability", "profile", "mapping", "explain", "environment"];

    public static Result<Skill> Parse(string text, string path, SkillSource source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var match = Frontmatter().Match(normalised);
        if (!match.Success)
        {
            return Result.Fail<Skill>(new Problem("skill.frontmatter_missing", $"{path}: a skill starts with frontmatter between '---' lines: name, description, version and applies_to."));
        }

        YamlMappingNode map;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(match.Groups["yaml"].Value));
            map = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode ?? new YamlMappingNode();
        }
        catch (YamlException exception)
        {
            return Result.Fail<Skill>(new Problem("skill.frontmatter_invalid", $"{path}: the frontmatter is not valid YAML (line {exception.Start.Line})."));
        }

        var problems = new List<Problem>();
        problems.AddRange(map.Children.Keys.OfType<YamlScalarNode>().Where(k => !Keys.Contains(k.Value, StringComparer.Ordinal))
            .Select(k => new Problem("skill.key_unknown", $"{path}: '{k.Value}' is not a frontmatter key. Known: {string.Join(", ", Keys)}.")));
        var name = Required(map, "name", path, problems);
        if (name is not null && !NamePattern().IsMatch(name))
        {
            problems.Add(new Problem("skill.name_invalid", $"{path}: the name '{name}' must be lower case words joined by dashes, such as supplier-assessment."));
        }

        var description = Required(map, "description", path, problems);
        var version = Required(map, "version", path, problems);
        var stepNames = List(map, "applies_to");
        if (stepNames.Count == 0)
        {
            problems.Add(new Problem("skill.field_missing", $"{path}: 'applies_to' is required: one or more of {string.Join(", ", Steps)}."));
        }

        problems.AddRange(stepNames.Where(s => !Steps.Contains(s, StringComparer.Ordinal))
            .Select(s => new Problem("skill.step_unknown", $"{path}: '{s}' is not an onboarding step; use {string.Join(", ", Steps)}.")));
        var body = match.Groups["body"].Value.Trim();
        if (body.Length > MaxBodyCharacters)
        {
            problems.Add(new Problem("skill.too_large", $"{path}: the body has {body.Length} characters; a skill holds at most {MaxBodyCharacters}. Split it into skills for different steps."));
        }

        return problems.Count > 0
            ? Result.Fail<Skill>(problems)
            : Result.Ok(new Skill(
                name!,
                description!,
                version!,
                [.. stepNames.Select(s => Enum.Parse<SkillStep>(s, ignoreCase: true))],
                List(map, "frameworks"),
                Scalar(map, "overrides"),
                body,
                source,
                path,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))));
    }

    private static string? Required(YamlMappingNode map, string key, string path, List<Problem> problems)
    {
        var value = Scalar(map, key);
        if (value is null)
        {
            problems.Add(new Problem("skill.field_missing", $"{path}: '{key}' is required."));
        }

        return value;
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { Length: > 0 } value } ? value.Trim() : null;

    private static List<string> List(YamlMappingNode map, string key) =>
        !map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? []
        : node is YamlSequenceNode items ? [.. items.OfType<YamlScalarNode>().Select(i => i.Value?.Trim()).OfType<string>().Where(v => v.Length > 0)]
        : node is YamlScalarNode { Value: { Length: > 0 } one } ? [.. one.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
        : [];

    [GeneratedRegex(@"\A---\n(?<yaml>.*?)\n---\n?(?<body>.*)\z", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
