namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-09: the incident response plan is derived from the repo, every value with its source.</summary>
public class IrpBuilderTests
{
    [Fact]
    public void Contacts_come_from_role_holders_with_sources()
    {
        var plan = IrpFixture.Build();

        plan.Contacts.Select(c => (c.Role, c.Holder.Value)).ShouldBe(
        [
            ("incident_manager", "it@example.com"),
            ("deputy", "alex@example.com"),
            ("executive", "ceo@example.com"),
            ("legal_privacy", "privacy@example.com"),
            ("authorising_officer", "ceo@example.com"),
        ]);
        plan.Contacts[0].Holder.Source.Reference.ShouldBe("scope/irp.yaml line 4");

        var supplier = plan.Parties.Single(p => p.Kind == "supplier");
        supplier.Name.Value.ShouldBe("Example Cloud Pty Ltd");
        supplier.Contacts.Select(c => c.Value).ShouldContain("account.manager@example.com");
        supplier.Contacts[0].Source.Reference.ShouldStartWith(SupplierScopeTests.SupplierPath);
        supplier.Clauses.Select(c => c.Value).ShouldBe(["DFARS 252.204-7012"]);
        var customer = plan.Parties.Single(p => p.Kind == "customer");
        customer.Name.Value.ShouldBe("Example Prime Pty Ltd");
        customer.Clauses.Select(c => c.Value).ShouldContain("DFARS 252.204-7012");

        var laptops = plan.Systems.Single(s => s.Name.Value == "Example laptops");
        laptops.Data.Select(d => d.Value).ShouldBe(["cui (CUI//SP-CTI), processes"]);
        laptops.Data[0].Source.Reference.ShouldStartWith("scope/setup.yaml line ");
    }

    [Fact]
    public void Reporting_obligations_list_each_deadline()
    {
        var reports = IrpFixture.Build().Reports;

        reports.Select(r => (r.Name.Value, r.Within.Value, r.Trigger.Value)).ShouldBe(
        [
            ("DFARS 252.204-7012 cyber incident report", "72 hours", "an incident on a system handling cui (Example laptops)"),
            ("Notifiable Data Breaches assessment", "30 days", "an incident on a system handling pii (none in scope)"),
            ("Report to ASD's ACSC through ReportCyber", "24 hours", "every incident"),
        ]);
        reports[0].ReportTo.Value.ShouldBe("DoD, through the DIBNet portal");
        reports[0].Name.Source.Reference.ShouldStartWith("scope/reporting.yaml line ");
    }
}
