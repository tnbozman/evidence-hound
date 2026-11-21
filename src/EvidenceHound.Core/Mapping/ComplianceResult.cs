namespace EvidenceHound.Core.Mapping;

/// <summary>The assessed state of one framework requirement. Mirrors CISO Assistant's requirement assessment results.</summary>
public enum ComplianceResult
{
    Compliant,
    PartiallyCompliant,
    NonCompliant,
    NotAssessed,
    NotApplicable,
}
