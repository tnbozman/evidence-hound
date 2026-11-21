using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml;

/// <summary>A mapping parsed from a file, with any warnings worth showing to its author.</summary>
public sealed record LoadedMapping(FrameworkMapping Mapping, IReadOnlyList<string> Warnings)
{
    /// <summary>The file the mapping came from, as named in problems.</summary>
    public string Source { get; init; } = "";

    /// <summary>The line of each requirement, in requirement order.</summary>
    public IReadOnlyList<int> Lines { get; init; } = [];

    /// <summary>Requirements no check covers yet, each with a suggested manual source.</summary>
    public IReadOnlyList<ManualOnlyRequirement> ManualOnly { get; init; } = [];
}

/// <summary>A requirement no check covers yet, and the manual source suggested for it (<c>manual_only</c> in a mapping file).</summary>
public sealed record ManualOnlyRequirement(Mapping.RequirementKey Key, string Source, int Line);
