using System.Reflection;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Skills;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Onboarding.Agents;

/// <summary>The coding assistant a person uses in VS Code.</summary>
public enum Assistant
{
    Claude,
    Copilot,
    Both,
}

/// <summary>One assistant file, by its path relative to the compliance repo, with forward slashes.</summary>
public sealed record AgentFile(string Path, string Text);

/// <summary>What <c>hound agent setup</c> writes for an assistant, and the files of hound's it retires.</summary>
public sealed record AgentFiles(IReadOnlyList<AgentFile> Write, IReadOnlyList<string> Retire);

/// <summary>
/// The assistant files for a compliance repo, built from one source: this repository's <c>agent/</c> folder (the rules, the instructions,
/// the skills and the agents) with the default skills from <c>skills/</c> that each agent skill includes. Pure, given the source.
/// </summary>
public static partial class AgentKit
{
    internal const string ResourcePrefix = "agent/";

    private static readonly Dictionary<string, string[]> ClaudeTools = new(StringComparer.Ordinal)
    {
        ["read"] = ["Read"],
        ["search"] = ["Grep", "Glob"],
        ["edit"] = ["Edit", "Write"],
        ["execute"] = ["Bash"],
    };

    /// <summary>The files for <paramref name="assistant"/>, each marked as written by hound <paramref name="version"/>.</summary>
    public static Result<AgentFiles> Files(Assistant assistant, string version)
    {
        var assembly = typeof(AgentKit).Assembly;
        var source = assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .ToDictionary(n => n[ResourcePrefix.Length..], n => Read(assembly, n), StringComparer.Ordinal);
        return SkillCatalogue.Defaults().Bind(defaults => Files(assistant, version, source, defaults));
    }

    /// <summary>
    /// The files for <paramref name="assistant"/> from <paramref name="source"/> (<c>rules.md</c>, <c>instructions.md</c>, <c>skills/*.md</c>,
    /// <c>agents/*.md</c>). With both assistants the skills are written once, under <c>.claude/skills/</c>, which Copilot reads too.
    /// </summary>
    public static Result<AgentFiles> Files(Assistant assistant, string version, IReadOnlyDictionary<string, string> source, IReadOnlyList<Skill> defaults)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(defaults);
        var problems = new List<Problem>();
        var skills = Parts(source, "skills/", problems);
        var agents = Parts(source, "agents/", problems);
        var guidance = defaults.ToDictionary(d => d.Name, StringComparer.Ordinal);
        var names = skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(skills.SelectMany(s => s.List("includes").Where(i => !guidance.ContainsKey(i))
            .Select(i => new Problem("agent.include_unknown", $"agent/skills/{s.Name}.md includes '{i}', which is not a default skill."))));
        problems.AddRange(agents.SelectMany(a => a.List("skills").Where(s => !names.Contains(s))
            .Select(s => new Problem("agent.skill_unknown", $"agent/agents/{a.Name}.md names the skill '{s}', which agent/skills/ does not hold."))));
        problems.AddRange(agents.SelectMany(a => a.List("tools").Where(t => !ClaudeTools.ContainsKey(t))
            .Select(t => new Problem("agent.tool_unknown", $"agent/agents/{a.Name}.md names the tool '{t}'; use {string.Join(", ", ClaudeTools.Keys)}."))));
        if (problems.Count > 0)
        {
            return Result.Fail<AgentFiles>(problems);
        }

        var opening = $"{Marker(version)}\n\n{source.GetValueOrDefault("rules.md", "").Trim()}";
        var instructions = $"{opening}\n\n{source.GetValueOrDefault("instructions.md", "").Trim()}\n";
        string SkillText(Part s) =>
            $"---\nname: {s.Name}\ndescription: {Quoted(s.Description)}\n---\n{opening}\n\n{string.Join("\n\n", [s.Body, .. s.List("includes").Select(i => guidance[i].Body)])}\n";
        IEnumerable<AgentFile> SkillFiles(string folder) => skills.Select(s => new AgentFile($"{folder}/skills/{s.Name}/SKILL.md", SkillText(s)));

        IEnumerable<AgentFile> claude =
        [
            new("CLAUDE.md", instructions),
            .. SkillFiles(".claude"),
            .. agents.Select(a => new AgentFile(
                $".claude/agents/{a.Name}.md",
                $"---\nname: {a.Name}\ndescription: {Quoted(a.Description)}\ntools: {string.Join(", ", a.List("tools").SelectMany(t => ClaudeTools[t]))}\n"
                + $"skills:\n{string.Concat(a.List("skills").Select(s => $"  - {s}\n"))}---\n{opening}\n\n{a.Body}\n")),
        ];
        IEnumerable<AgentFile> copilot =
        [
            new(".github/copilot-instructions.md", instructions),
            .. agents.Select(a => new AgentFile(
                $".github/agents/{a.Name}.agent.md",
                $"---\nname: {a.Name}\ndescription: {Quoted(a.Description)}\ntools: {Inline(a.List("tools"))}\nskills: {Inline(a.List("skills"))}\n---\n{opening}\n\n{a.Body}\n")),
        ];
        var copilotSkills = SkillFiles(".github").ToArray();
        return Result.Ok(assistant switch
        {
            Assistant.Claude => new AgentFiles([.. claude], []),
            Assistant.Copilot => new AgentFiles([.. copilot, .. copilotSkills], []),
            _ => new AgentFiles([.. claude, .. copilot], [.. copilotSkills.Select(f => f.Path)]),
        });
    }

    /// <summary>The first line hound puts in a file's body: whoever removes it takes the file over.</summary>
    public static string Marker(string version) =>
        $"<!-- Written by hound agent setup (hound {version}). Re-running it replaces this file; delete this line to keep your own version. -->";

    /// <summary>Whether <paramref name="text"/> still carries hound's marker, at any version.</summary>
    public static bool WrittenByHound(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return MarkerPattern().IsMatch(text);
    }

    private static List<Part> Parts(IReadOnlyDictionary<string, string> source, string folder, List<Problem> problems)
    {
        var parts = new List<Part>();
        foreach (var (path, text) in source.Where(s => s.Key.StartsWith(folder, StringComparison.Ordinal)).OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var match = Frontmatter().Match(text.Replace("\r\n", "\n", StringComparison.Ordinal));
            var map = match.Success ? Yaml(match.Groups["yaml"].Value) : null;
            var name = map is null ? null : Scalar(map, "name");
            var description = map is null ? null : Scalar(map, "description");
            if (map is null || name is null || description is null)
            {
                problems.Add(new Problem("agent.frontmatter_invalid", $"agent/{path}: needs frontmatter between '---' lines with a name and a description."));
                continue;
            }

            parts.Add(new Part(name, description, map, match.Groups["body"].Value.Trim()));
        }

        return parts;
    }

    private static YamlMappingNode? Yaml(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { Length: > 0 } value } ? value.Trim() : null;

    private static string Quoted(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string Inline(IEnumerable<string> values) => $"[{string.Join(", ", values.Select(Quoted))}]";

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\A---\n(?<yaml>.*?)\n---\n?(?<body>.*)\z", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"<!-- Written by hound agent setup \(hound [^)]*\)\.", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    /// <summary>A skill or an agent in the source: its frontmatter and body.</summary>
    private sealed record Part(string Name, string Description, YamlMappingNode Map, string Body)
    {
        public IReadOnlyList<string> List(string key) =>
            Map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode items
                ? [.. items.OfType<YamlScalarNode>().Select(i => i.Value?.Trim()).OfType<string>().Where(v => v.Length > 0)]
                : [];
    }
}
