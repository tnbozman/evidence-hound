using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s15-08: a customer contract records the clauses a prime imposes, each with the framework and level it requires; a clause marked
/// <c>flow_down</c> must be on every supplier contract whose solutions handle its data kinds.
/// </summary>
public class FlowDownTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    public const string Prime = """
        ref_id: CUS-PRIME
        name: Example Prime Pty Ltd
        domain: Example Pty Ltd
        contracts:
          - ref_id: CTR-PRIME-PARTS
            name: Machined parts subcontract
            status: active
            customer: true
            obligations:
              - clause: DFARS 252.204-7012
                framework: urn:example:risk:framework:example-baseline
                level: "2"
                data: [cui]
                flow_down: true
              - clause: Essential Eight maturity level 2
                framework: urn:example:risk:framework:example-baseline
                level: "2"
        """;

    // The supplier's hosting handles CUI, so its contract must carry the prime's DFARS clause.
    private static readonly string Supplier = SupplierScopeTests.Supplier.Replace(
        "    criticality: 3\n",
        "    criticality: 3\n    data:\n      - kind: cui\n        role: stores\n",
        StringComparison.Ordinal);

    [Fact]
    public void Supplier_contract_without_a_flowed_down_clause_is_reported()
    {
        var problem = Problems(Prime, Supplier).Where(p => p.Code == "contract.flow_down_missing").ShouldHaveSingleItem();
        problem.At.File.ShouldBe(SupplierScopeTests.SupplierPath);
        problem.Message.ShouldContain("CTR-EXCLOUD-2026");
        problem.Message.ShouldContain("DFARS 252.204-7012");
        problem.Message.ShouldContain("CTR-PRIME-PARTS");

        var carried = Supplier.Replace(
            "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n",
            "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n    obligations:\n      - clause: DFARS 252.204-7012\n",
            StringComparison.Ordinal);
        Problems(Prime, carried).ShouldNotContain(p => p.Code == "contract.flow_down_missing");
        Problems(Prime, SupplierScopeTests.Supplier).ShouldNotContain(p => p.Code == "contract.flow_down_missing", "a supplier that handles no CUI owes no DFARS clause");
    }

    [Fact]
    public void Customer_obligation_names_a_framework_and_level()
    {
        var parsed = Parse(Prime, Supplier);

        var contract = parsed.Scope.ThirdParties.Single(e => e.RefId == "CUS-PRIME").Contracts.ShouldHaveSingleItem();
        contract.Customer.ShouldBeTrue();
        contract.Obligations.Select(o => (o.Clause, o.Framework?.Value, o.Level, o.FlowDown)).ShouldBe(
        [
            ("DFARS 252.204-7012", "urn:example:risk:framework:example-baseline", "2", true),
            ("Essential Eight maturity level 2", "urn:example:risk:framework:example-baseline", "2", false),
        ]);
        contract.Obligations[0].Data.ShouldBe([SensitiveDataKind.Cui]);
        Problems(Prime.Replace("framework: urn:example:risk:framework:example-baseline\n        level: \"2\"\n        data", "framework: urn:example:risk:framework:not-declared\n        level: \"2\"\n        data", StringComparison.Ordinal), Supplier)
            .ShouldContain(p => p.Code == "contract.framework_unknown");
    }

    [Fact]
    public void Flow_down_on_a_supplier_contract_is_refused()
    {
        var flowing = SupplierScopeTests.Supplier.Replace(
            "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n",
            "    solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]\n    obligations:\n      - clause: DFARS 252.204-7012\n        flow_down: true\n",
            StringComparison.Ordinal);

        Problems(flowing).ShouldContain(p => p.Code == "contract.flow_down_invalid");
    }

    private static ScopeParse Parse(params string[] entities) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties: [.. entities.Select((s, i) => new ScopeSource(s.StartsWith("ref_id: SUP-", StringComparison.Ordinal) ? SupplierScopeTests.SupplierPath : $"scope/third-parties/prime-{i}.yaml", s))]);

    private static IReadOnlyList<ScopeProblem> Problems(params string[] entities)
    {
        var parsed = Parse(entities);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []), .. SupplierValidator.Validate(parsed.Scope, Today)];
    }
}
