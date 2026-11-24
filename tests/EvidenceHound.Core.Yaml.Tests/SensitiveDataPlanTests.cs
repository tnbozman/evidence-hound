using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s10-06: sensitive data reaches CISO Assistant through its own features: filtering labels, and the authority in the solution's description.</summary>
public class SensitiveDataPlanTests
{
    [Fact]
    public void Kinds_become_filtering_labels()
    {
        var desired = ScopeDesired.From(SensitiveDataScopeTests.Parse(SensitiveDataScopeTests.Supplier(), SensitiveDataScopeTests.Setup()).Scope);

        var solution = desired.Single(o => o.Kind == ScopeKind.Solution);
        solution.Fields["labels"].ShouldBe("data-cui, data-pii");
        solution.Fields["description"].ShouldContain("cui (CUI//SP-CTI), stores: FedRAMP Moderate equivalent, evidence suppliers/example-cloud-fedramp.pdf, expires 2027-06-30");
        solution.Fields["description"].ShouldContain("pii, stores: no authority recorded");
        desired.Single(o => o.Kind == ScopeKind.System && o.Fields.GetValueOrDefault("labels") is not null).Fields["labels"].ShouldBe("data-cui");
    }

    [Fact]
    public void A_solution_and_a_system_without_data_carry_no_labels()
    {
        var desired = ScopeDesired.From(SensitiveDataScopeTests.Parse(SupplierScopeTests.Supplier, ScopeFixture.Setup).Scope);

        desired.Where(o => o.Kind is ScopeKind.Solution or ScopeKind.System).ShouldAllBe(o => !o.Fields.ContainsKey("labels"));
    }
}
