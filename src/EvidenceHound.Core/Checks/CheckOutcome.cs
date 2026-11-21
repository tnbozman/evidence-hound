namespace EvidenceHound.Core.Checks;

/// <summary>What a single check found.</summary>
public enum CheckOutcome
{
    Pass,
    Fail,
    Partial,
    NotApplicable,
    Error,
}
