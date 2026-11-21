using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Tests.Checks;

public class CheckResultTests
{
    private static readonly CheckId MfaAdmins = CheckTestData.Id("identity.mfa.admins");

    [Fact]
    public void Valid_result_keeps_every_field()
    {
        var result = CheckResult.Create(
            MfaAdmins, CheckOutcome.Pass, "All 4 admin accounts have MFA.", ["ev-1", "ev-2"], CheckTestData.CollectedAt, "example-idp").Value();

        result.CheckId.ShouldBe(MfaAdmins);
        result.Outcome.ShouldBe(CheckOutcome.Pass);
        result.Observation.ShouldBe("All 4 admin accounts have MFA.");
        result.EvidenceRefs.ShouldBe(["ev-1", "ev-2"]);
        result.CollectedAt.ShouldBe(CheckTestData.CollectedAt);
        result.Source.ShouldBe("example-idp");
    }

    [Fact]
    public void Observation_of_2000_chars_is_accepted()
    {
        var result = CheckResult.Create(MfaAdmins, CheckOutcome.Fail, new string('x', 2000), [], CheckTestData.CollectedAt, "example-idp");

        result.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Observation_longer_than_2000_chars_is_rejected()
    {
        var result = CheckResult.Create(MfaAdmins, CheckOutcome.Fail, new string('x', 2001), [], CheckTestData.CollectedAt, "example-idp");

        result.Problems().Single().Code.ShouldBe("check_result.observation_too_long");
    }

    [Fact]
    public void Missing_source_or_evidence_ref_is_rejected_with_every_problem()
    {
        var result = CheckResult.Create(MfaAdmins, CheckOutcome.Pass, "ok", ["ev-1", " "], CheckTestData.CollectedAt, "");

        result.Problems().Select(p => p.Code).ShouldBe(["check_result.evidence_ref_empty", "check_result.source_empty"], ignoreOrder: true);
    }

    [Fact]
    public void Outcomes_are_the_five_standard_values()
    {
        Enum.GetNames<CheckOutcome>().ShouldBe(["Pass", "Fail", "Partial", "NotApplicable", "Error"]);
    }
}
