using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>
/// s12-01: the checks a person attests, read from manual collectors: a hound config (<c>collectors:</c>) or a manual source template
/// (a list of collectors, as under <c>examples/manual-sources/</c>). Only collectors with <c>type: manual</c> count.
/// </summary>
public static class ManualSourceChecks
{
    public static Result<IReadOnlyList<CheckId>> Read(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<IReadOnlyList<CheckId>>(new Problem("manual.yaml_invalid", $"{source}: not valid YAML: {ex.Message}"));
        }

        var root = stream.Documents.FirstOrDefault()?.RootNode;
        var collectors = root switch
        {
            YamlSequenceNode list => list,
            YamlMappingNode map when map.Children.TryGetValue(new YamlScalarNode("collectors"), out var node) => node as YamlSequenceNode,
            _ => null,
        };
        var names = (collectors ?? [])
            .OfType<YamlMappingNode>()
            .Where(c => Text(c, "type") == "manual")
            .SelectMany(c => (Text(Map(c, "settings"), "checks") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return Result.Combine(names.Select(name => CheckId.Create(name).Match(
            Result.Ok,
            problems => Result.Fail<CheckId>(problems.Select(p => new Problem(p.Code, $"{source}: {p.Message}"))))));
    }

    private static YamlMappingNode? Map(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child as YamlMappingNode : null;

    private static string? Text(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(new YamlScalarNode(key), out var child) ? (child as YamlScalarNode)?.Value : null;
}
