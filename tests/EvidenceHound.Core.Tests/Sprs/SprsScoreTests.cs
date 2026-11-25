using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Sprs;

namespace EvidenceHound.Core.Tests.Sprs;

/// <summary>s17-05: the NIST SP 800-171 DoD Assessment Methodology score, worked out from a CMMC Level 2 audit's rows.</summary>
public class SprsScoreTests
{
    private static readonly SprsMethodology Methodology = new("NIST SP 800-171 DoD Assessment Methodology", "1.2.1", 110,
    [
        new SprsWeight("3.1.1", 5),
        new SprsWeight("3.1.3", 1),
        new SprsWeight("3.1.12", 5) { NotApplicableWhen = "remote access is not permitted" },
        new SprsWeight("3.5.3", 5) { Partial = new SprsPartial(3, "MFA is implemented for remote and privileged users, but not for general users") },
        new SprsWeight("3.12.4", null) { Note = "Without a system security plan, an assessment could not be completed." },
    ]);

    [Fact]
    public void Unmet_requirements_deduct_their_weights()
    {
        var score = SprsScoring.Score(Methodology,
        [
            new SprsRow("AC.L1-3.1.1", ComplianceResult.NonCompliant),
            new SprsRow("AC.L2-3.1.3", ComplianceResult.Compliant),
            new SprsRow("AC.L2-3.1.12", ComplianceResult.NotApplicable),
            new SprsRow("IA.L2-3.5.3", ComplianceResult.PartiallyCompliant),
            new SprsRow("CA.L2-3.12.4", ComplianceResult.Compliant),
        ]);

        score.Score.ShouldBe(100);
        score.Deductions.Select(d => (d.Requirement, d.Practice, d.Points)).ShouldBe([("3.1.1", "AC.L1-3.1.1", 5), ("3.5.3", "IA.L2-3.5.3", 5)]);
        score.Deductions[1].Note.ShouldBe("3 points instead of 5 if MFA is implemented for remote and privileged users, but not for general users; a person decides.");
        score.SspMissing.ShouldBeFalse();
    }

    [Fact]
    public void A_requirement_not_assessed_or_missing_from_the_audit_deducts_its_points()
    {
        var score = SprsScoring.Score(Methodology,
        [
            new SprsRow("AC.L2-3.1.3", ComplianceResult.NotAssessed),
            new SprsRow("AC.L2-3.1.12", ComplianceResult.Compliant),
            new SprsRow("IA.L2-3.5.3", ComplianceResult.Compliant),
            new SprsRow("CA.L2-3.12.4", ComplianceResult.Compliant),
        ]);

        score.Score.ShouldBe(104);
        score.Deductions.Select(d => (d.Requirement, d.Practice, d.Result)).ShouldBe([("3.1.1", null, (ComplianceResult?)null), ("3.1.3", "AC.L2-3.1.3", ComplianceResult.NotAssessed)]);
        score.Deductions[0].Note.ShouldBe("Not in the audit.");
    }

    [Fact]
    public void Without_a_system_security_plan_the_score_is_flagged()
    {
        var score = SprsScoring.Score(Methodology,
        [
            new SprsRow("AC.L1-3.1.1", ComplianceResult.Compliant),
            new SprsRow("AC.L2-3.1.3", ComplianceResult.Compliant),
            new SprsRow("AC.L2-3.1.12", ComplianceResult.Compliant),
            new SprsRow("IA.L2-3.5.3", ComplianceResult.Compliant),
            new SprsRow("CA.L2-3.12.4", ComplianceResult.PartiallyCompliant),
        ]);

        score.Score.ShouldBe(110);
        score.SspMissing.ShouldBeTrue();
        score.Deductions.ShouldBeEmpty();
    }
}
