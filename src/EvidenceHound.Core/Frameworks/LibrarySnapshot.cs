using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Frameworks;

/// <summary>
/// What risk studies need to know about a library without asking CISO Assistant: its risk matrices' level names and its threats, and the
/// libraries it brings with it. Generated with <c>hound library snapshot</c> and committed.
/// </summary>
public sealed record LibrarySnapshot(
    string LibraryUrn,
    int Version,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<MatrixSnapshot> Matrices,
    IReadOnlyList<Threat> Threats);

/// <summary>A risk matrix's URN, name and level names, lowest first.</summary>
public sealed record MatrixSnapshot(string Urn, string Name, IReadOnlyList<string> Probability, IReadOnlyList<string> Impact, IReadOnlyList<string> Risk);
