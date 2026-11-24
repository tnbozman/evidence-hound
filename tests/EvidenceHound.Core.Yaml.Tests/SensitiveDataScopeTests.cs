using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s10-06: a solution and a system say which sensitive data they handle (CUI, personal information, export-controlled data, classified
/// information) and, for a solution, what authorises it, so a supplier handling data it is not approved for is reported.
/// </summary>
public class SensitiveDataScopeTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    // A solution's keys sit at four spaces in SupplierScopeTests.Supplier, a system's at eight in ScopeFixture.Setup.
    private const string SolutionData =
        "    criticality: 3\n" +
        "    data:\n" +
        "      - kind: cui\n" +
        "        detail: CUI//SP-CTI\n" +
        "        role: stores\n" +
        "        authority:\n" +
        "          name: FedRAMP Moderate equivalent\n" +
        "          evidence: suppliers/example-cloud-fedramp.pdf\n" +
        "          expires: \"2027-06-30\"\n" +
        "      - kind: pii\n" +
        "        role: stores\n";

    private const string SystemData =
        "        owner: it@example.com\n" +
        "        solution: SOL-EXCLOUD-HOST\n" +
        "        supplied_through: SUP-EXCLOUD\n" +
        "        data:\n" +
        "          - kind: cui\n" +
        "            detail: CUI//SP-CTI\n" +
        "            role: stores\n" +
        "        tags:\n";

    [Fact]
    public void Kinds_parse_on_a_solution_and_a_system()
    {
        var parsed = Parse(Supplier(), Setup());

        parsed.Problems.ShouldBeEmpty();
        var solution = parsed.Scope.ThirdParties.Single().Solutions[0];
        var cui = solution.Data[0];
        (cui.Kind, cui.Detail, cui.Role, cui.Authority?.Name, cui.Authority?.Evidence, cui.Authority?.Expires)
            .ShouldBe((SensitiveDataKind.Cui, "CUI//SP-CTI", DataRole.Stores, "FedRAMP Moderate equivalent", "suppliers/example-cloud-fedramp.pdf", new DateOnly(2027, 6, 30)));
        solution.Data[1].Kind.ShouldBe(SensitiveDataKind.Pii);
        parsed.Scope.Setup.Domains.Single().Systems[0].Data.Single().Kind.ShouldBe(SensitiveDataKind.Cui);
        Problems(Supplier(), Setup()).ShouldBeEmpty();
    }

    [Fact]
    public void System_with_cui_through_an_unauthorised_supplier_is_reported()
    {
        var supplier = Supplier().Replace("kind: cui\n        detail: CUI//SP-CTI\n        role: stores", "kind: export_controlled\n        detail: ITAR\n        role: stores", StringComparison.Ordinal);

        var problem = Problems(supplier, Setup()).ShouldHaveSingleItem();

        (problem.Code, problem.At.File).ShouldBe(("data.supplier_not_authorised", "scope/setup.yaml"));
        problem.Message.ShouldContain("SOL-EXCLOUD-HOST");
        problem.Message.ShouldContain("cui");
    }

    [Fact]
    public void Expired_authority_is_reported()
    {
        var problem = Problems(Supplier().Replace("\"2027-06-30\"", "\"2026-06-30\"", StringComparison.Ordinal), Setup()).ShouldHaveSingleItem();

        (problem.Code, problem.At.File).ShouldBe(("data.authority_expired", SupplierScopeTests.SupplierPath));
        problem.Message.ShouldContain("2026-06-30");
    }

    [Theory]
    [InlineData("kind: cui\n            detail: CUI//SP-CTI\n            role: stores", "kind: classified\n            role: stores", "data.detail_missing")]
    [InlineData("kind: cui\n            detail: CUI//SP-CTI\n            role: stores", "kind: export_controlled\n            role: stores", "data.detail_missing")]
    [InlineData("kind: cui", "kind: secret_sauce", "scope.value_invalid")]
    [InlineData("role: stores\n        tags:", "role: guards\n        tags:", "scope.value_invalid")]
    public void Classified_and_export_controlled_need_detail(string from, string to, string code)
    {
        Problems(Supplier(), Setup().Replace(from, to, StringComparison.Ordinal)).Select(p => p.Code).ShouldContain(code);
    }

    private static string Supplier() => SupplierScopeTests.Supplier.Replace("    criticality: 3\n", SolutionData, StringComparison.Ordinal);

    private static string Setup() => ScopeFixture.Setup.Replace("        owner: it@example.com\n        tags:\n", SystemData, StringComparison.Ordinal);

    private static ScopeParse Parse(string supplier, string setup) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties: [new ScopeSource(SupplierScopeTests.SupplierPath, supplier)]);

    private static IReadOnlyList<ScopeProblem> Problems(string supplier, string setup)
    {
        var parsed = Parse(supplier, setup);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []), .. SupplierValidator.Validate(parsed.Scope, Today)];
    }
}
