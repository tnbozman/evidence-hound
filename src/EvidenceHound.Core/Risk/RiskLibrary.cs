namespace EvidenceHound.Core.Risk;

/// <summary>One level of a risk matrix scale. The library format numbers levels by position; <paramref name="Id"/> is an explicit number, if given.</summary>
public sealed record RiskLevel(int? Id, string Name, string? Abbreviation);

/// <summary>
/// A risk matrix from a CISO Assistant library: its probability, impact and risk scales, and the grid that gives the risk level for each
/// probability (row) and impact (cell).
/// </summary>
public sealed record RiskMatrix(
    string Urn,
    string? RefId,
    string Name,
    IReadOnlyList<RiskLevel> Probability,
    IReadOnlyList<RiskLevel> Impact,
    IReadOnlyList<RiskLevel> Risk,
    IReadOnlyList<IReadOnlyList<int>> Grid);

/// <summary>A threat from a library's threat catalogue.</summary>
public sealed record Threat(string Urn, string? RefId, string Name);

/// <summary>A library's URN and version, and the SHA-256 of its file when known, as the version check compares them.</summary>
public sealed record LibraryVersion(string Urn, int Version, string? Sha256);
