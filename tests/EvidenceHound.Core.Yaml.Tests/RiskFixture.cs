using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-02: a fictional risk study for Example Pty Ltd on the example 4x4 matrix, with ways to break it.</summary>
internal static class RiskFixture
{
    public const string Path = "scope/risks/ex-risk-2026.yaml";

    public const string Study = """
        ref_id: EX-RISK-2026
        name: Example Pty Ltd risk register 2026
        domain: Example Pty Ltd
        perimeter: Example baseline
        matrix: urn:example:risk:matrix:example-4x4
        status: in_progress
        authors: [it@example.com]
        eta: "2026-12-31"
        scenarios:
          - ref_id: R-01
            name: Ransomware encrypts the file shares
            description: Staff files are encrypted and held to ransom.
            systems: [Example 365]
            threats: [THR-02]
            existing_controls: [AC-BACKUP]
            planned_controls: []
            current: {probability: Possible, impact: Major}
            residual: {probability: Unlikely, impact: Major}
            treatment: mitigate
            justification: Backups are restored and tested each month.
          - ref_id: R-02
            name: The only backup administrator leaves
            threats: [urn:example:risk:threat:example-risk:thr-01]
            current: {probability: Unlikely, impact: Moderate}
            treatment: accept
        """;

    public static IReadOnlyDictionary<string, LibrarySnapshot> Libraries { get; } = new Dictionary<string, LibrarySnapshot>
    {
        ["urn:example:risk:library:example-risk"] = RiskLibraryReader.Snapshot(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-risk-library.yaml")), "example-risk-library.yaml").Value(),
    };

    public static ScopeParse Parse(string study = Study) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            risks: [new ScopeSource(Path, study)]);

    public static IReadOnlyList<ScopeProblem> Problems(string study = Study)
    {
        var parsed = Parse(study);
        return [.. parsed.Problems, .. RiskValidator.Validate(parsed.Scope, Libraries)];
    }

    /// <summary>The 1-based line of the first line of <see cref="Study"/> (or <paramref name="study"/>) that contains <paramref name="text"/>.</summary>
    public static int LineOf(string text, string study = Study) =>
        study.Split('\n').Select((line, i) => (line, i)).First(x => x.line.Contains(text, StringComparison.Ordinal)).i + 1;
}
