using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Tests.Mapping;

public class NamesTests
{
    [Fact]
    public void Every_outcome_and_result_has_a_snake_case_name()
    {
        Enum.GetValues<CheckOutcome>().Select(Names.Of).ShouldBe(["pass", "fail", "partial", "not_applicable", "error"]);
        Enum.GetValues<ComplianceResult>().Select(Names.Of)
            .ShouldBe(["compliant", "partially_compliant", "non_compliant", "not_assessed", "not_applicable"]);
    }

    [Fact]
    public void Undefined_values_are_bugs()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Names.Of((CheckOutcome)99));
        Should.Throw<ArgumentOutOfRangeException>(() => Names.Of((ComplianceResult)99));
    }

    [Fact]
    public void Rules_have_their_file_names()
    {
        new[] { MappingRule.AllPass, MappingRule.AnyPass, MappingRule.Threshold(2) }.Select(r => r.Name)
            .ShouldBe(["all_pass", "any_pass", "threshold(2)"]);
    }
}
