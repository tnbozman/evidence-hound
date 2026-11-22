using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s09-19: <c>scope/third-parties/&lt;entity&gt;.yaml</c> declares one supplier with its solutions, contracts and representatives.</summary>
public class SupplierScopeTests
{
    public const string SupplierPath = "scope/third-parties/example-cloud.yaml";

    private static readonly DateOnly Today = new(2026, 11, 22);

    public const string Supplier = """
        ref_id: SUP-EXCLOUD
        name: Example Cloud Pty Ltd
        description: Hosts the customer portal.
        mission: Managed cloud hosting.
        country: AU
        currency: AUD
        domain: Example Pty Ltd
        dependency: 3
        penetration: 2
        maturity: 3
        trust: 3
        identifiers:
          LEI: EXAMPLE0000000000000
        solutions:
          - ref_id: SOL-EXCLOUD-HOST
            name: Managed hosting
            description: The customer portal's servers.
            criticality: 3
          - ref_id: SOL-EXCLOUD-BACKUP
            name: Offsite backup
            criticality: 2
        contracts:
          - ref_id: CTR-EXCLOUD-2026
            name: Hosting agreement 2026
            status: active
            start: "2026-01-01"
            end: "2026-12-31"
            annual_expense: 12000
            currency: AUD
            solutions: [SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]
        representatives:
          - email: account.manager@example.com
            first_name: Sam
            last_name: Example
            role: Account manager
        """;

    [Fact]
    public void Example_supplier_parses_with_its_solutions_contracts_and_representatives()
    {
        var parsed = Parse(Supplier);

        Problems(Supplier).ShouldBeEmpty();
        var entity = parsed.Scope.ThirdParties.ShouldHaveSingleItem();
        (entity.RefId, entity.Name, entity.Country, entity.Currency, entity.Domain?.Value).ShouldBe(("SUP-EXCLOUD", "Example Cloud Pty Ltd", "AU", "AUD", "Example Pty Ltd"));
        (entity.Dependency, entity.Penetration, entity.Maturity, entity.Trust).ShouldBe((3, 2, 3, 3));
        entity.Identifiers.ShouldBe(new Dictionary<string, string> { ["LEI"] = "EXAMPLE0000000000000" });
        entity.Solutions.Select(s => (s.RefId, s.Criticality)).ShouldBe([("SOL-EXCLOUD-HOST", 3), ("SOL-EXCLOUD-BACKUP", 2)]);
        var contract = entity.Contracts.ShouldHaveSingleItem();
        (contract.Status, contract.Start, contract.End, contract.AnnualExpense, contract.Currency).ShouldBe(("active", "2026-01-01", "2026-12-31", "12000", "AUD"));
        contract.Solutions.Select(s => s.Value).ShouldBe(["SOL-EXCLOUD-HOST", "SOL-EXCLOUD-BACKUP"]);
        entity.Representatives.ShouldHaveSingleItem().Email.ShouldBe("account.manager@example.com");
    }

    [Theory]
    [InlineData("country: AU", "country: XX", "supplier.country_invalid", 5)]
    [InlineData("country: AU", "country: AUS", "supplier.country_invalid", 5)]
    [InlineData("currency: AUD\ndomain", "currency: ABC\ndomain", "supplier.currency_invalid", 6)]
    [InlineData("    currency: AUD\n", "    currency: XYZ\n", "supplier.currency_invalid", 29)]
    public void Invalid_country_or_currency_is_reported(string from, string to, string code, int line)
    {
        var problem = Problems(Supplier.Replace(from, to, StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe((code, new SourceLine(SupplierPath, line)));
        problem.Hint.ShouldContain("ISO");
    }

    [Theory]
    [InlineData("dependency: 3", "dependency: 5")]
    [InlineData("maturity: 3", "maturity: 0")]
    [InlineData("criticality: 3", "criticality: 5")]
    public void A_level_out_of_its_range_is_reported(string from, string to)
    {
        Problems(Supplier.Replace(from, to, StringComparison.Ordinal)).ShouldHaveSingleItem().Code.ShouldBe("supplier.level_invalid");
    }

    [Fact]
    public void Active_contract_past_end_date_is_reported()
    {
        var lapsed = Supplier.Replace("end: \"2026-12-31\"", "end: \"2026-10-31\"", StringComparison.Ordinal);

        var problem = Problems(lapsed).ShouldHaveSingleItem();
        (problem.Code, problem.At.Line).ShouldBe(("supplier.contract_lapsed", 27));
        problem.Message.ShouldContain("CTR-EXCLOUD-2026");
        problem.Message.ShouldContain("2026-10-31");
        Problems(lapsed.Replace("status: active", "status: expired", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Contract_ending_before_it_starts_is_reported()
    {
        var problem = Problems(Supplier.Replace("start: \"2026-01-01\"", "start: \"2027-01-01\"", StringComparison.Ordinal)).ShouldHaveSingleItem();

        problem.Code.ShouldBe("supplier.contract_dates_invalid");
    }

    [Fact]
    public void Contract_solution_must_belong_to_entity()
    {
        var problem = Problems(Supplier.Replace("[SOL-EXCLOUD-HOST, SOL-EXCLOUD-BACKUP]", "[SOL-EXCLOUD-HOST, SOL-OTHER]", StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("supplier.solution_unknown", 30));
        problem.Message.ShouldContain("SOL-OTHER");
    }

    [Fact]
    public void Unknown_status_parent_and_domain_are_reported()
    {
        var broken = Supplier
            .Replace("status: active", "status: signed", StringComparison.Ordinal)
            .Replace("domain: Example Pty Ltd", "domain: Nowhere\nparent: SUP-NOWHERE", StringComparison.Ordinal);

        Problems(broken).Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["scope.reference_unknown", "scope.reference_unknown", "scope.value_invalid"]);
    }

    [Theory]
    [InlineData("tprm", "SUP-EXCLOUD")]
    [InlineData("contracts", "CTR-EXCLOUD-2026")]
    public void Turning_off_the_flag_that_shows_them_is_reported(string flag, string named)
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            null,
            [],
            settings: new ScopeSource("scope/settings.yaml", $"feature_flags:\n  {flag}: false\n"),
            thirdParties: [new ScopeSource(SupplierPath, Supplier)]);

        var problem = ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []).ShouldHaveSingleItem();
        problem.Code.ShouldBe("settings.feature_disabled_in_use");
        problem.Message.ShouldContain(named);
    }

    [Fact]
    public void A_supplier_ref_id_is_declared_once()
    {
        var parsed = Parse(Supplier, Supplier);

        ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []).ShouldContain(p => p.Code == "scope.duplicate" && p.Message.Contains("SUP-EXCLOUD", StringComparison.Ordinal));
    }

    [Fact]
    public void Desired_objects_are_the_entity_its_solutions_contracts_and_representatives_by_ref_id_or_email()
    {
        var objects = ScopeDesired.From(Parse(Supplier).Scope).Where(o => o.Kind is ScopeKind.Entity or ScopeKind.Solution or ScopeKind.Contract or ScopeKind.Representative).ToArray();

        objects.Select(o => (o.Kind, o.Key)).ShouldBe(
        [
            (ScopeKind.Entity, "SUP-EXCLOUD"),
            (ScopeKind.Solution, "SOL-EXCLOUD-HOST"),
            (ScopeKind.Solution, "SOL-EXCLOUD-BACKUP"),
            (ScopeKind.Contract, "CTR-EXCLOUD-2026"),
            (ScopeKind.Representative, "account.manager@example.com"),
        ]);
        objects[0].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Cloud Pty Ltd",
            ["description"] = "Hosts the customer portal.",
            ["mission"] = "Managed cloud hosting.",
            ["country"] = "AU",
            ["currency"] = "AUD",
            ["domain"] = "Example Pty Ltd",
            ["dependency"] = "3",
            ["penetration"] = "2",
            ["maturity"] = "3",
            ["trust"] = "3",
            ["identifiers"] = "{\"LEI\":\"EXAMPLE0000000000000\"}",
        }, ignoreOrder: true);
        objects[3].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Hosting agreement 2026",
            ["entity"] = "SUP-EXCLOUD",
            ["domain"] = "Example Pty Ltd",
            ["status"] = "active",
            ["start_date"] = "2026-01-01",
            ["end_date"] = "2026-12-31",
            ["annual_expense"] = "12000",
            ["currency"] = "AUD",
            ["solutions"] = "SOL-EXCLOUD-BACKUP, SOL-EXCLOUD-HOST",
        }, ignoreOrder: true);
        objects[1].Fields["entity"].ShouldBe("SUP-EXCLOUD");
        objects[4].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["entity"] = "SUP-EXCLOUD",
            ["first_name"] = "Sam",
            ["last_name"] = "Example",
            ["role"] = "Account manager",
        }, ignoreOrder: true);
    }

    internal static readonly string OrganisationSetup = ScopeFixture.Setup.Replace(
        "  root_folder: Global\n",
        "  root_folder: Global\n  entity:\n    name: Example Pty Ltd\n    description: The organisation.\n    country: AU\n    currency: AUD\n    identifiers:\n      ABN: EXAMPLE-ABN\n",
        StringComparison.Ordinal);

    [Fact]
    public void The_organisations_own_entity_is_declared_in_setup_and_only_ever_updated()
    {
        var parsed = ScopeLoader.Parse(new ScopeSource("scope/setup.yaml", OrganisationSetup), null, []);

        parsed.Problems.ShouldBeEmpty();
        var main = ScopeDesired.From(parsed.Scope).Single(o => o.Kind == ScopeKind.MainEntity);
        main.Key.ShouldBe("organisation");
        main.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Pty Ltd", ["description"] = "The organisation.", ["country"] = "AU", ["currency"] = "AUD", ["identifiers"] = "{\"ABN\":\"EXAMPLE-ABN\"}",
        }, ignoreOrder: true);
        SupplierValidator.Validate(parsed.Scope with { Setup = parsed.Scope.Setup with { Organisation = parsed.Scope.Setup.Organisation! with { Country = "XX" } } }, Today)
            .ShouldHaveSingleItem().Code.ShouldBe("supplier.country_invalid");
        var plan = ScopePlanner.Plan([main], [], prune: true);
        plan.Changes.ShouldHaveSingleItem().Action.ShouldBe(ScopeAction.Create);
        ScopePlanner.Plan([], [new ActualObject(main, null)], prune: true).Changes.ShouldBeEmpty();
    }

    private static ScopeParse Parse(params string[] suppliers) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties: [.. suppliers.Select((s, i) => new ScopeSource(i == 0 ? SupplierPath : $"scope/third-parties/copy-{i}.yaml", s))]);

    private static IReadOnlyList<ScopeProblem> Problems(string supplier)
    {
        var parsed = Parse(supplier);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []), .. SupplierValidator.Validate(parsed.Scope, Today)];
    }
}
