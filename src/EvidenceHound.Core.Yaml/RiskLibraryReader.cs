using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Risk;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads a CISO Assistant library file's risk matrices, threats and dependencies, and checks its matrices offline.</summary>
public static partial class RiskLibraryReader
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

    /// <summary>
    /// The digest of the library's <c>objects</c> (<see cref="LibraryContent"/>), with each plain scalar typed as CISO Assistant's YAML loader
    /// types it (null, boolean, number or text), so it matches the stored library's; null when the file is not valid YAML.
    /// </summary>
    public static string? ContentDigest(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException)
        {
            return null;
        }

        return stream.Documents.FirstOrDefault()?.RootNode is YamlMappingNode root ? LibraryContent.Digest(Typed(Child(root, "objects"))) : null;
    }

    private static readonly HashSet<string> Nulls = new(["", "~", "null", "Null", "NULL"], StringComparer.Ordinal);
    private static readonly HashSet<string> Trues = new(["true", "True", "TRUE", "yes", "Yes", "YES", "on", "On", "ON", "y", "Y"], StringComparer.Ordinal);
    private static readonly HashSet<string> Falses = new(["false", "False", "FALSE", "no", "No", "NO", "off", "Off", "OFF", "n", "N"], StringComparer.Ordinal);

    private static System.Text.Json.Nodes.JsonNode? Typed(YamlNode? node) => node switch
    {
        YamlMappingNode map => new System.Text.Json.Nodes.JsonObject(map.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", Typed(c.Value)))),
        YamlSequenceNode list => new System.Text.Json.Nodes.JsonArray([.. list.Children.Select(Typed)]),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: var plain } => Plain(plain ?? ""),
        YamlScalarNode scalar => System.Text.Json.Nodes.JsonValue.Create(scalar.Value ?? ""),
        _ => null,
    };

    private static System.Text.Json.Nodes.JsonValue? Plain(string value) =>
        Nulls.Contains(value) ? null
        : Trues.Contains(value) ? System.Text.Json.Nodes.JsonValue.Create(true)
        : Falses.Contains(value) ? System.Text.Json.Nodes.JsonValue.Create(false)
        : NumberPattern().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? System.Text.Json.Nodes.JsonValue.Create(number)
        : System.Text.Json.Nodes.JsonValue.Create(value);

    /// <summary>A YAML 1.1 integer or a float with a decimal point, as Python's YAML loader reads them.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^[-+]?([0-9]+|[0-9]*\.[0-9]+([eE][-+]?[0-9]+)?|[0-9]+\.[0-9]*([eE][-+]?[0-9]+)?)$")]
    private static partial System.Text.RegularExpressions.Regex NumberPattern();

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
