using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Setup;

/// <summary>A value to write into a YAML file.</summary>
internal abstract record YamlValue;

/// <summary>One scalar, quoted only when YAML would read it as something else.</summary>
internal sealed record YamlText(string Value) : YamlValue;

/// <summary>A short list of scalars, written on one line: <c>[a, b]</c>.</summary>
internal sealed record YamlFlowList(IReadOnlyList<string> Items) : YamlValue;

/// <summary>A map, in the order given.</summary>
internal sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlValue>> Entries) : YamlValue;

/// <summary>Adds to a YAML file in place, keeping its comments and every other line as they were.</summary>
internal static class YamlEdit
{
    public static Result<string> AppendItem(string yaml, string path, YamlMap item) =>
        Result.Fail<string>(new Problem("setup.not_implemented", $"{yaml.Length} {path} {item}"));

    public static Result<string> AppendScalar(string yaml, string path, string value) =>
        Result.Fail<string>(new Problem("setup.not_implemented", $"{yaml.Length} {path} {value}"));
}
