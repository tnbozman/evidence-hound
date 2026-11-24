using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-02: the example baseline as a programme, with a system security plan declared for it, and what CISO Assistant holds.</summary>
internal static class SspFixture
{
    public const string SspPath = "scope/ssp.yaml";

    public const string Access = """
        users:
          - email: it@example.com
            first_name: Alex
            last_name: Example
          - email: ceo@example.com
            first_name: Sam
            last_name: Example
        teams:
          - name: GRC team
            domain: Example Pty Ltd
            leader: it@example.com
        """;

    public const string Ssp = """
        ssps:
          - programme: baseline
            profile: ism
            system_name: Example corporate IT
            perimeter: Example baseline
            version: "0.1"
            classification: OFFICIAL
            operational_status: operational
            system_type: general support system
            roles:
              system_owner: it@example.com
              authorising_official: ceo@example.com
              security_contact: GRC team
            documents: [mfa-policy]
            sections:
              purpose: ssp/purpose.md
              environment: ssp/environment.md
              management: ssp/management.md
              laws_and_policies: ssp/laws-and-policies.md
        """;

    /// <summary>The fixture's setup with the baseline programme, and Example 365 provided by a supplier's solution.</summary>
    public static readonly string Setup = ScopeFixture.Setup.Replace(
        "        owner: it@example.com\n        tags:",
        "        owner: it@example.com\n        solution: SOL-EXCLOUD-HOST\n        tags:",
        StringComparison.Ordinal) + "\nprogrammes:\n" + ProgrammeScopeTests.Programme();

    public static ScopeParse Parse(string ssp = Ssp, string setup = "") =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup.Length == 0 ? Setup : setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            access: new ScopeSource("scope/access.yaml", Access),
            thirdParties: [new ScopeSource("scope/third-parties/sup-excloud.yaml", SupplierScopeTests.Supplier)],
            ssp: new ScopeSource(SspPath, ssp));

    public static IReadOnlyList<ScopeProblem> Problems(string ssp = Ssp)
    {
        var parsed = Parse(ssp);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, [])];
    }

    /// <summary>What CISO Assistant holds for the fixture: the two systems, the supplier and its solution, the controls and the audit's rows.</summary>
    public static IReadOnlyList<ActualObject> Held() =>
    [
        Object(ScopeKind.System, "Example 365", ("ref_id", "SYS-365"), ("type", "primary"), ("owner", "it@example.com"), ("domain", "Example Pty Ltd"), ("description", "Mail, files and identity.")),
        Object(ScopeKind.System, "Example laptops", ("type", "supporting"), ("owner", "ceo@example.com"), ("parents", "Example 365")),
        Object(ScopeKind.System, "Somebody else's server", ("type", "primary"), ("owner", "it@example.com")),
        Object(ScopeKind.Entity, "SUP-EXCLOUD", ("name", "Example Cloud Pty Ltd"), ("country", "AU")),
        Object(ScopeKind.Solution, "SOL-EXCLOUD-HOST", ("name", "Managed hosting"), ("entity", "SUP-EXCLOUD"), ("criticality", "3")),
        Object(ScopeKind.Control, "AC-MFA", ("name", "Multi-factor authentication"), ("owner", "it@example.com"), ("systems", "Example 365"), ("description", "Every account uses MFA."), ("eta", "2026-12-31")) with
        {
            Observed = new Dictionary<string, string> { ["status"] = "active" },
        },
        Object(ScopeKind.Control, "AC-BACKUP", ("name", "Daily backups"), ("systems", "Example laptops")),
        Object(ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.1", ("status", "done"), ("result", "compliant"), ("observation", "All 12 accounts use MFA."), ("controls", "AC-MFA"), ("documents", "mfa-policy")),
        Object(ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.2", ("status", "done"), ("result", "partially_compliant"), ("controls", "AC-MFA")),
        Object(ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-2.1", ("status", "to_do"), ("result", "not_assessed")),
        Object(ScopeKind.Assessment, "OTHER-AUDIT/EX-1.1", ("result", "non_compliant")),
    ];

    private static ActualObject Object(ScopeKind kind, string key, params (string Name, string Value)[] fields) =>
        new(new ScopeObject(kind, key, fields.ToDictionary(f => f.Name, f => f.Value)), "managed");
}
