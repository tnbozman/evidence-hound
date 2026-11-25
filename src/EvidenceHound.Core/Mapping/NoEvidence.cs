namespace EvidenceHound.Core.Mapping;

/// <summary>
/// s15-14: a row with no evidence is not compliant. CISO Assistant's compliance percentage counts compliant (and half of partially
/// compliant) over compliant, partially and non-compliant rows only, so a not-assessed row drops out and a few passes read as 100%.
/// hound sends such a row as non-compliant, saying why, unless the audit opts out.
/// </summary>
public static class NoEvidence
{
    public const string Prefix = "No evidence yet: ";

    /// <summary>What the observation says when the row had none of its own.</summary>
    public const string Nothing = "nothing is recorded for this requirement.";

    /// <summary>
    /// The result and observation a row held in CISO Assistant was declared with: a row hound sent non-compliant for want of evidence was
    /// declared not assessed, with the observation after the prefix (none when it had none of its own).
    /// </summary>
    public static (string? Result, string? Observation) Declared(string? result, string? observation) =>
        result == Names.Of(ComplianceResult.NonCompliant) && observation is not null && observation.StartsWith(Prefix, StringComparison.Ordinal)
            ? (Names.Of(ComplianceResult.NotAssessed), observation[Prefix.Length..] is var own && own != Nothing ? own : null)
            : (result, observation);

    /// <summary>The observation for a row with no evidence, applied as <paramref name="result"/>.</summary>
    public static string? Observation(ComplianceResult result, string? observation) =>
        result == ComplianceResult.NotAssessed || (observation?.StartsWith(Prefix, StringComparison.Ordinal) ?? false)
            ? observation
            : Prefix + (string.IsNullOrWhiteSpace(observation) ? Nothing : observation);
}
