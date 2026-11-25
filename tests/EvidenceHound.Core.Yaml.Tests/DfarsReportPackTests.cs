using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s17-07: a CUI incident starts the 72-hour DFARS 252.204-7012 report. The pack gathers what the report asks for from CISO Assistant and
/// the scope (affected systems, data kinds, dates, suppliers and the primes' contracts) for the person who files it on the DoD portal.
/// </summary>
public class DfarsReportPackTests
{
    private static readonly DateTimeOffset Discovered = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private static readonly ReportingObligation Dfars = new(
        "dfars-7012", "DFARS 252.204-7012 cyber incident report", "DoD, through the DIBNet portal", new Duration(72 * 3600), [SensitiveDataKind.Cui], false, new SourceLine("scope/reporting.yaml", 2))
    {
        Reference = "DFARS 252.204-7012(c)",
    };

    [Fact]
    public void Gathers_systems_suppliers_and_contracts_for_a_cui_incident()
    {
        var incident = new IncidentFacts("INC-2026-07", "Malware on a laptop", Discovered, ["Example 365"], [new IncidentEntry(Discovered, "Detected by the endpoint agent.")]);

        var pack = DfarsReportPack.Markdown(incident, Scope(), [Dfars], Discovered.AddHours(5));

        pack.ShouldStartWith("# DFARS 252.204-7012 report pack: INC-2026-07 Malware on a laptop\n");
        pack.ShouldContain("- Discovered: 2026-11-20 09:30 UTC\n- Report due: 2026-11-23 09:30 UTC (72 hours, DFARS 252.204-7012(c)): due\n");
        pack.ShouldContain("| Example 365 | cui (stores) |");
        pack.ShouldContain("| Example 365 | Managed hosting | Example Cloud Pty Ltd | CTR-EXCLOUD-2026 |");
        pack.ShouldContain("| CTR-PRIME-PARTS | Machined parts subcontract | Example Prime Pty Ltd | DFARS 252.204-7012 |");
        pack.ShouldContain("| 2026-11-20 09:30 UTC | Detected by the endpoint agent. |");
        pack.ShouldContain("90 days");
        pack.ShouldContain("Reported: dfars-7012, <DIBNet report number>");
    }

    [Fact]
    public void An_incident_on_no_cui_system_owes_no_dfars_report()
    {
        var incident = new IncidentFacts("INC-2026-08", "Phishing email", Discovered, ["Example laptops"], []);

        var pack = DfarsReportPack.Markdown(incident, Scope(), [Dfars], Discovered.AddHours(80));

        pack.ShouldContain("No affected system handles CUI under scope/setup.yaml, so no DFARS 252.204-7012 report is owed.");
    }

    private static ScopeDocument Scope()
    {
        var setup = ScopeFixture.Setup.Replace(
            "        owner: it@example.com\n        tags:\n          essentials_domain: identity\n",
            "        owner: it@example.com\n        solution: SOL-EXCLOUD-HOST\n        data:\n          - kind: cui\n            role: stores\n        tags:\n          essentials_domain: identity\n",
            StringComparison.Ordinal);
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties: [new ScopeSource(SupplierScopeTests.SupplierPath, SupplierScopeTests.Supplier), new ScopeSource("scope/third-parties/cus-prime.yaml", FlowDownTests.Prime)]);
        parsed.Problems.ShouldBeEmpty();
        return parsed.Scope;
    }
}
