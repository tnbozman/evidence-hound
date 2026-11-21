using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Mapping;

/// <summary>The lowercase snake_case names used in files, observations and output.</summary>
public static class Names
{
    public static string Of(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Pass => "pass",
        CheckOutcome.Fail => "fail",
        CheckOutcome.Partial => "partial",
        CheckOutcome.NotApplicable => "not_applicable",
        CheckOutcome.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string Of(ComplianceResult result) => result switch
    {
        ComplianceResult.Compliant => "compliant",
        ComplianceResult.PartiallyCompliant => "partially_compliant",
        ComplianceResult.NonCompliant => "non_compliant",
        ComplianceResult.NotAssessed => "not_assessed",
        ComplianceResult.NotApplicable => "not_applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
