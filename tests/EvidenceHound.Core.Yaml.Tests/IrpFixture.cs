using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-09: the fictional Example Pty Ltd's incident response plan, with a CUI system, a supplier and a prime.</summary>
internal static class IrpFixture
{
    public const string Irp = """
        programme: essential-eight
        version: "0.1"
        roles:
          incident_manager: it@example.com
          deputy: alex@example.com
          executive: ceo@example.com
          legal_privacy: privacy@example.com
          authorising_officer: ceo@example.com
        sections:
          severity: irp/severity.md
          escalation: irp/escalation.md
          containment: irp/containment.md
          communication: irp/communication.md
          lessons_learned: irp/lessons-learned.md
        """;

    public static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["irp/severity.md"] = "A severity 1 incident stops the business.",
        ["irp/escalation.md"] = "The incident manager calls the executive within the hour.",
        ["irp/containment.md"] = "Isolate the laptop first.",
        ["irp/communication.md"] = "One spokesperson.",
        ["irp/lessons-learned.md"] = "A review within two weeks.",
    };

    private static readonly string Setup = ScopeFixture.Setup.Replace(
        "        parents: [Example 365]\n",
        "        parents: [Example 365]\n        data:\n          - kind: cui\n            detail: CUI//SP-CTI\n            role: processes\n",
        StringComparison.Ordinal);

    private static readonly string Supplier = SupplierScopeTests.Supplier.Replace(
        "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n",
        "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n    obligations:\n      - clause: DFARS 252.204-7012\n",
        StringComparison.Ordinal);

    public static ScopeIrp Plan(string irp = Irp) => ScopeLoader.ParseIrp(new ScopeSource("scope/irp.yaml", irp)).Irp!;

    public static ScopeDocument Scope() =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties:
            [
                new ScopeSource(SupplierScopeTests.SupplierPath, Supplier),
                new ScopeSource("scope/third-parties/cus-prime.yaml", FlowDownTests.Prime.Replace("ref_id: CUS-PRIME", "ref_id: CUS-EXAMPLE-PRIME", StringComparison.Ordinal)),
            ]).Scope;

    public static IReadOnlyList<ReportingObligation> Reporting() => ScopeLoader.ParseReporting(new ScopeSource("scope/reporting.yaml", ReportingObligationTests.Reporting)).Obligations;

    public static IrpDocument Build(string irp = Irp, IReadOnlyDictionary<string, string>? texts = null) => IrpBuilder.Build(Plan(irp), Scope(), Reporting(), texts ?? Texts);
}
