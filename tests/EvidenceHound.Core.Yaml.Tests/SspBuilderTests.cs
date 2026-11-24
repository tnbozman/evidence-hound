using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-02: the system security plan is assembled from what CISO Assistant holds and what the repo declares, and says where each value came from.</summary>
public class SspBuilderTests
{
    [Fact]
    public void Inventory_is_the_perimeter_assets_with_owners()
    {
        var document = Build();

        document.Inventory.Select(s => (s.Name.Value, s.Type.Value, s.Owner.Value)).ShouldBe(
            [("Example 365", "primary", "it@example.com"), ("Example laptops", "supporting", "ceo@example.com")]);
        document.Inventory[0].Description.Value.ShouldBe("Mail, files and identity.");
        document.Inventory[0].Owner.Source.ShouldBe(new SspSource(SspSource.CisoAssistant, "asset 'Example 365'"));
    }

    [Fact]
    public void A_perimeter_system_missing_from_ciso_assistant_is_listed_without_values()
    {
        var held = SspFixture.Held().Where(o => o.Held.Key != "Example laptops").ToArray();

        var laptops = SspBuilder.Build(Ssp(), SspFixture.Parse().Scope, held).Inventory.Single(s => s.Name.Value == "Example laptops");

        laptops.Owner.Value.ShouldBeNull();
        laptops.Name.Source.Kind.ShouldBe(SspSource.Repo);
    }

    [Fact]
    public void External_services_come_from_linked_solutions()
    {
        var service = Build().ExternalServices.ShouldHaveSingleItem();

        (service.System.Value, service.Solution.Value, service.Supplier.Value, service.Criticality.Value).ShouldBe(("Example 365", "Managed hosting", "Example Cloud Pty Ltd", "3"));
        service.System.Source.Reference.ShouldStartWith("scope/setup.yaml line ");
        service.Solution.Source.ShouldBe(new SspSource(SspSource.CisoAssistant, "solution SOL-EXCLOUD-HOST"));
        service.Supplier.Source.ShouldBe(new SspSource(SspSource.CisoAssistant, "entity SUP-EXCLOUD"));
    }

    [Fact]
    public void Controls_and_requirements_come_from_the_programme_audit()
    {
        var document = Build();

        document.Requirements.Select(r => (r.Ref.Value, r.Result.Value)).ShouldBe([("EX-1.1", "compliant"), ("EX-1.2", "partially_compliant"), ("EX-2.1", "not_assessed")]);
        document.Requirements[0].Evidence.Value.ShouldBe("mfa-policy");
        document.Requirements[0].Result.Source.ShouldBe(new SspSource(SspSource.CisoAssistant, "requirement assessment EXAMPLE-BASELINE-2026/EX-1.1"));
        var control = document.Controls.ShouldHaveSingleItem();
        (control.RefId.Value, control.Owner.Value, control.Status.Value).ShouldBe(("AC-MFA", "it@example.com", "active"));
        control.Description.Value.ShouldBe("Every account uses MFA.");
        control.Eta.Value.ShouldBe("2026-12-31");
    }

    [Fact]
    public void Requirements_carry_their_status_statement_and_owner()
    {
        var document = Build();

        var row = document.Requirements[0];
        (row.Status.Value, row.Statement.Value, row.Responsible.Value).ShouldBe(("Implemented", "AC-MFA: Every account uses MFA.\nAll 12 accounts use MFA.", "it@example.com"));
        document.Requirements[1].Status.Value.ShouldBe("Partially implemented");
        document.Gaps.ShouldBeEmpty();
    }

    [Fact]
    public void Roles_documents_and_sections_come_from_the_repo()
    {
        var document = Build();

        document.Roles.Select(r => (r.Role, r.Holder.Value)).ShouldBe([("system_owner", "it@example.com"), ("authorising_official", "ceo@example.com"), ("security_contact", "GRC team")]);
        document.Roles.ShouldAllBe(r => r.Holder.Source.Kind == SspSource.Repo);
        document.Documents.ShouldHaveSingleItem().Value.ShouldBe("MFA policy");
        document.Sections.Select(s => (s.Name, s.Value.Value)).ShouldBe(
            [("purpose", "ssp/purpose.md"), ("environment", "ssp/environment.md"), ("management", "ssp/management.md"), ("laws_and_policies", "ssp/laws-and-policies.md")]);
        (document.Classification.Value, document.OperationalStatus.Value, document.SystemType.Value).ShouldBe(("OFFICIAL", "operational", "general support system"));
        (document.Framework.Value, document.Audit.Value, document.Perimeter.Value).ShouldBe((ScopeFixture.FrameworkUrn, "EXAMPLE-BASELINE-2026", "Example baseline"));
    }

    [Fact]
    public void Every_value_records_its_source()
    {
        var json = SspJson.Write(Build());

        var values = Values(JsonNode.Parse(json)!).ToArray();
        values.ShouldNotBeEmpty();
        values.ShouldAllBe(v => v["source"]!["kind"] != null && v["source"]!["reference"] != null);
        values.Select(v => v["source"]!["kind"]!.GetValue<string>()).Distinct().Order().ShouldBe(["ciso_assistant", "repo"]);
        var row = JsonNode.Parse(json)!["requirements"]![0]!;
        row["status"]!["value"]!.GetValue<string>().ShouldBe("Implemented");
        row["gaps"]!.AsArray().ShouldBeEmpty();
        JsonNode.Parse(json)!["gaps"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public void The_annex_lists_each_control_in_the_programme_groups_by_parent()
    {
        var annex = SspBuilder.Build(Ssp(), SspFixture.Parse().Scope, SspFixture.Held(), ScopeFixture.Snapshot).Annex;

        // The programme reports against groups 1 and 2, so EX-2.1 is in the annex too, not yet assessed and on the POA&M.
        annex.Select(a => (a.Control, a.Status.Value, a.Poam)).ShouldBe(
            [("EX-1.1", "Implemented", null), ("EX-1.2", "Partially implemented", "POAM-EX-1.2"), ("EX-2.1", "Not implemented", "POAM-EX-2.1")]);
        annex[0].Parents.ShouldBe(["(top level)"]);
    }

    private static SspDocument Build() => SspBuilder.Build(Ssp(), SspFixture.Parse().Scope, SspFixture.Held());

    private static ScopeSsp Ssp() => SspFixture.Parse().Scope.Ssps.Single();

    /// <summary>Every object in the JSON that holds a value: it has a "value" key.</summary>
    private static IEnumerable<JsonObject> Values(JsonNode node) => node switch
    {
        JsonObject o when o.ContainsKey("value") => [o],
        JsonObject o => o.Select(p => p.Value).OfType<JsonNode>().SelectMany(Values),
        JsonArray a => a.OfType<JsonNode>().SelectMany(Values),
        _ => [],
    };
}
