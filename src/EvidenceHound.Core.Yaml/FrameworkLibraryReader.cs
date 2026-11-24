using System.Globalization;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads a CISO Assistant library file and makes a snapshot of its framework.</summary>
public static partial class FrameworkLibraryReader
{
    public static Result<FrameworkSnapshot> Snapshot(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<FrameworkSnapshot>(new Problem("framework.yaml_invalid", $"{source}: not valid YAML: {ex.Message}"));
        }

        var root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        var framework = root is null ? null : Map(Map(root, "objects"), "framework");
        if (root is null || framework is null)
        {
            return Result.Fail<FrameworkSnapshot>(new Problem("framework.library_has_no_framework", $"{source}: the library has no framework."));
        }

        return Result.Ok(new FrameworkSnapshot(
            Text(root, "urn") ?? "",
            Text(framework, "urn") ?? "",
            int.TryParse(Text(root, "version"), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0,
            [.. Items(framework, "implementation_groups_definition").Select(g => new ImplementationGroup(Text(g, "ref_id") ?? "", Text(g, "name") ?? ""))],
            [.. Items(framework, "requirement_nodes").Where(n => Text(n, "assessable") == "true").Select(Node)]));
    }

    private static SnapshotNode Node(YamlMappingNode node) => new(
        Text(node, "urn") ?? "",
        Text(node, "ref_id"),
        Text(node, "parent_urn"),
        [.. (Child(node, "implementation_groups") as YamlSequenceNode ?? []).OfType<YamlScalarNode>().Select(g => g.Value ?? "")],
        Title(Text(node, "name") ?? Text(node, "description") ?? ""))
    {
        Description = OneLine(Text(node, "description") ?? Text(node, "name") ?? ""),
        ReferenceControls = [.. (Child(node, "reference_controls") as YamlSequenceNode ?? []).OfType<YamlScalarNode>().Select(r => r.Value ?? "").Where(r => r.Length > 0)],
    };

    private static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>The text on one line, cut at a word to at most <see cref="FrameworkSnapshot.TitleLength"/> characters.</summary>
    private static string Title(string text)
    {
        var line = OneLine(text);
        if (line.Length <= FrameworkSnapshot.TitleLength)
        {
            return line;
        }

        var cut = line[..(FrameworkSnapshot.TitleLength - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > 0 ? cut[..space] : cut).TrimEnd(',', ';', ':', '.') + "…";
    }

    private static YamlMappingNode? Map(YamlMappingNode? map, string key) => map is null ? null : Child(map, key) as YamlMappingNode;

    private static IEnumerable<YamlMappingNode> Items(YamlMappingNode map, string key) =>
        (Child(map, key) as YamlSequenceNode ?? []).OfType<YamlMappingNode>();

    private static string? Text(YamlMappingNode map, string key) => (Child(map, key) as YamlScalarNode)?.Value;

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
