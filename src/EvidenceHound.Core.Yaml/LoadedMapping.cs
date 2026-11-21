using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml;

/// <summary>A mapping parsed from a file, with any warnings worth showing to its author.</summary>
public sealed record LoadedMapping(FrameworkMapping Mapping, IReadOnlyList<string> Warnings)
{
    /// <summary>The file the mapping came from, as named in problems.</summary>
    public string Source { get; init; } = "";

    /// <summary>The line of each requirement, in requirement order.</summary>
    public IReadOnlyList<int> Lines { get; init; } = [];
}
