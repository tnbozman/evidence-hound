using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml;

/// <summary>A mapping parsed from a file, with any warnings worth showing to its author.</summary>
public sealed record LoadedMapping(FrameworkMapping Mapping, IReadOnlyList<string> Warnings);
