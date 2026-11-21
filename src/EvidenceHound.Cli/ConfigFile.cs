using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EvidenceHound.Cli;

/// <summary>Reads a deployment config file (I/O edge) and parses it into a <see cref="HoundConfig"/>.</summary>
internal static class ConfigFile
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static async Task<ConfigLoad> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new ConfigLoad.Failed($"Config file not found: {path}");
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(text, path);
    }

    public static ConfigLoad Parse(string yaml, string source)
    {
        try
        {
            var document = Deserializer.Deserialize<ConfigDocument?>(yaml) ?? new ConfigDocument();
            var names = (document.Collectors ?? []).Select(c => c.Name ?? "").ToList();
            var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".";
            var target = document.Target is { Mapping: { } mapping, ComplianceAssessment: { } audit } t
                ? new TargetConfig(Path.GetFullPath(Path.Combine(folder, mapping)), audit, t.Controls ?? [])
                : null;
            return new ConfigLoad.Loaded(new HoundConfig(names, target));
        }
        catch (YamlException ex)
        {
            return new ConfigLoad.Failed($"Config file {source} is not valid YAML at line {ex.Start.Line}: {ex.Message}");
        }
    }

    // Mutable DTOs exist only for YamlDotNet; they never leave this class.
    private sealed class ConfigDocument
    {
        public List<CollectorDocument>? Collectors { get; set; }

        public TargetDocument? Target { get; set; }
    }

    private sealed class TargetDocument
    {
        public string? Mapping { get; set; }

        public string? ComplianceAssessment { get; set; }

        public Dictionary<string, string>? Controls { get; set; }
    }

    private sealed class CollectorDocument
    {
        public string? Name { get; set; }
    }
}
