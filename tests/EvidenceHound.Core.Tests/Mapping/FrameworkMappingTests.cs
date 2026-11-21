using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Mapping;

public class FrameworkMappingTests
{
    private static readonly string Urn = "urn:example:risk:library:example-baseline";

    private static readonly CheckId MfaAdmins = CheckTestData.Id("identity.mfa.admins");
    private static readonly CheckId MfaUsers = CheckTestData.Id("identity.mfa.users");

    [Fact]
    public void Valid_mapping_keeps_requirements_in_file_order()
    {
        var mapping = FrameworkMapping.Create(Urn, [
            RequirementMapping.Create("2.1", [MfaAdmins], MappingRule.AllPass).Value(),
            RequirementMapping.Create("1.1", [MfaAdmins, MfaUsers], MappingRule.Threshold(2)).Value(),
        ]).Value();

        mapping.FrameworkUrn.ShouldBe(Urn);
        mapping.Requirements.Select(r => r.RequirementRef).ShouldBe(["2.1", "1.1"]);
        mapping.Requirements[1].Checks.ShouldBe([MfaAdmins, MfaUsers]);
        mapping.Requirements[1].Rule.ShouldBe(MappingRule.Threshold(2));
    }

    [Fact]
    public void Duplicate_requirement_refs_are_rejected()
    {
        var mapping = FrameworkMapping.Create(Urn, [
            RequirementMapping.Create("1.1", [MfaAdmins], MappingRule.AllPass).Value(),
            RequirementMapping.Create("1.1", [MfaUsers], MappingRule.AnyPass).Value(),
        ]);

        mapping.Problems().Single().Code.ShouldBe("mapping.duplicate_requirement_ref");
    }

    [Theory]
    [InlineData("")]
    [InlineData("example-baseline")]
    public void Framework_urn_must_be_a_urn(string urn)
    {
        FrameworkMapping.Create(urn, []).Problems().Single().Code.ShouldBe("mapping.framework_urn_invalid");
    }

    [Fact]
    public void Requirement_needs_a_ref_and_at_least_one_check()
    {
        RequirementMapping.Create(" ", [], MappingRule.AllPass).Problems().Select(p => p.Code)
            .ShouldBe(["mapping.requirement_ref_empty", "mapping.checks_empty"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Threshold_must_be_between_one_and_the_number_of_checks(int n)
    {
        RequirementMapping.Create("1.1", [MfaAdmins, MfaUsers], MappingRule.Threshold(n)).Problems().Single().Code
            .ShouldBe("mapping.threshold_out_of_range");
    }

    [Fact]
    public void Duplicate_check_in_one_requirement_is_rejected()
    {
        RequirementMapping.Create("1.1", [MfaAdmins, MfaAdmins], MappingRule.AnyPass).Problems().Single().Code
            .ShouldBe("mapping.duplicate_check");
    }

    [Fact]
    public void Rules_are_equal_by_value()
    {
        MappingRule.Threshold(2).ShouldBe(MappingRule.Threshold(2));
        MappingRule.Threshold(2).ShouldNotBe(MappingRule.Threshold(3));
        MappingRule.AllPass.ShouldNotBe(MappingRule.AnyPass);
    }
}
