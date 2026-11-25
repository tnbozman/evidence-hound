using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Risk;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads a CISO Assistant library file's risk matrices, threats and dependencies, and checks its matrices offline.</summary>
public static class RiskLibraryReader
{
    /// <summary>The library's snapshot: its matrices' level names, its threats and the libraries it brings with it.</summary>
    public static Result<LibrarySnapshot> Snapshot(string yaml, string source) =>
        Read(yaml, source).Map(library => new LibrarySnapshot(
            library.Urn,
            library.Version,
            library.Dependencies,
            [.. library.Matrices.Select(m => new MatrixSnapshot(m.Urn, m.Name, Names(m.Probability), Names(m.Impact), Names(m.Risk)))],
            library.Threats));

    /// <summary>Every problem with the library's risk matrices, each starting with <paramref name="source"/>; empty when there are none.</summary>
    public static IReadOnlyList<Problem> Validate(string yaml, string source) =>
        Read(yaml, source).Match(
            library => [.. library.Matrices.SelectMany(m => RiskMatrixValidation.Check(m, library.Packager)).Select(p => new Problem(p.Code, $"{source}: {p.Message}"))],
            problems => problems);

    private static Result<Library> Read(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<Library>(new Problem("library.yaml_invalid", $"{source}: not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root || Text(root, "urn") is not { } urn)
        {
            return Result.Fail<Library>(new Problem("library.urn_missing", $"{source}: a library needs a 'urn'."));
        }

        var objects = Child(root, "objects") as YamlMappingNode;
        return Result.Ok(new Library(
            urn,
            int.TryParse(Text(root, "version"), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0,
            Text(root, "packager") ?? "",
            [.. Scalars(root, "dependencies")],
            objects is null ? [] : [.. Items(objects, "risk_matrix").Select(Matrix)],
            objects is null ? [] : [.. Items(objects, "threats").Select(t => new Threat(Text(t, "urn") ?? "", Text(t, "ref_id"), Text(t, "name") ?? ""))]));
    }

    private static RiskMatrix Matrix(YamlMappingNode node) => new(
        Text(node, "urn") ?? "",
        Text(node, "ref_id"),
        Text(node, "name") ?? "",
        Levels(node, "probability"),
        Levels(node, "impact"),
        Levels(node, "risk"),
        [.. (Child(node, "grid") as YamlSequenceNode ?? []).Select(row => (IReadOnlyList<int>)[.. (row as YamlSequenceNode ?? []).OfType<YamlScalarNode>().Select(Number)])]);

    private static RiskLevel[] Levels(YamlMappingNode node, string key) =>
        [.. Items(node, key).Select(l => new RiskLevel(Text(l, "id") is { } id ? Number(new YamlScalarNode(id)) : null, Text(l, "name") ?? "", Text(l, "abbreviation")))];

    /// <summary>A whole number, or -1 when the value is not one, so the grid check reports it.</summary>
    private static int Number(YamlScalarNode node) =>
        int.TryParse(node.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static string[] Names(IEnumerable<RiskLevel> levels) => [.. levels.Select(l => l.Name)];

    private static IEnumerable<string> Scalars(YamlMappingNode map, string key) =>
        (Child(map, key) as YamlSequenceNode ?? []).OfType<YamlScalarNode>().Select(s => s.Value ?? "").Where(s => s.Length > 0);

    private static IEnumerable<YamlMappingNode> Items(YamlMappingNode map, string key) =>
        (Child(map, key) as YamlSequenceNode ?? []).OfType<YamlMappingNode>();

    private static string? Text(YamlMappingNode map, string key) => (Child(map, key) as YamlScalarNode)?.Value;

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private sealed record Library(string Urn, int Version, string Packager, IReadOnlyList<string> Dependencies, IReadOnlyList<RiskMatrix> Matrices, IReadOnlyList<Threat> Threats);
}
