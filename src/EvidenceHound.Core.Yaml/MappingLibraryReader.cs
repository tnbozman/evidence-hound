using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>s14-01: reads a CISO Assistant requirement mapping library (<c>objects.requirement_mapping_set</c>).</summary>
public static class MappingLibraryReader
{
    private static readonly Dictionary<string, CrosswalkRelationship> Relationships = new(StringComparer.Ordinal)
    {
        ["equal"] = CrosswalkRelationship.Equal,
        ["superset"] = CrosswalkRelationship.Superset,
        ["subset"] = CrosswalkRelationship.Subset,
        ["intersect"] = CrosswalkRelationship.Intersect,
    };

    public static Result<MappingLibrary> Read(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<MappingLibrary>(new Problem("mapping_library.yaml_invalid", $"{source}: not valid YAML: {ex.Message}"));
        }

        var set = (stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode) is { } root && Child(root, "objects") is YamlMappingNode objects
            ? Child(objects, "requirement_mapping_set") as YamlMappingNode
            : null;
        if (set is null)
        {
            return Result.Fail<MappingLibrary>(new Problem("mapping_library.no_mapping_set", $"{source}: the library has no requirement_mapping_set."));
        }

        var links = (Child(set, "requirement_mappings") as YamlSequenceNode ?? []).OfType<YamlMappingNode>().Select(m => Link(m, source)).ToList();
        return Result.Combine(links).Map(list => new MappingLibrary(Text(set, "source_framework_urn") ?? "", Text(set, "target_framework_urn") ?? "", list));
    }

    private static Result<RequirementLink> Link(YamlMappingNode map, string source) =>
        (Text(map, "source_requirement_urn"), Text(map, "target_requirement_urn"), Text(map, "relationship")) switch
        {
            ({ } from, { } to, { } relationship) when Relationships.TryGetValue(relationship, out var kind) => Result.Ok(new RequirementLink(from, to, kind)),
            var (from, to, relationship) => Result.Fail<RequirementLink>(new Problem(
                "mapping_library.link_invalid",
                $"{source} line {map.Start.Line}: a requirement mapping needs a source and target URN and a relationship of equal, superset, subset or intersect "
                + $"(found {from ?? "no source"}, {to ?? "no target"}, {relationship ?? "no relationship"})."))
        };

    private static string? Text(YamlMappingNode map, string key) => (Child(map, key) as YamlScalarNode)?.Value;

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
}
