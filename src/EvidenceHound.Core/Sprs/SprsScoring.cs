using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Sprs;

/// <summary>The methodology's partial deduction for a requirement (3.5.3, 3.13.11): fewer points when a stated case applies.</summary>
public sealed record SprsPartial(int Points, string When);

/// <summary>What one NIST SP 800-171 requirement takes off the score when not implemented; no points for 3.12.4, the plan itself.</summary>
public sealed record SprsWeight(string Requirement, int? Points)
{
    public SprsPartial? Partial { get; init; }

    /// <summary>When the methodology says not to subtract points, such as when remote access is not permitted.</summary>
    public string? NotApplicableWhen { get; init; }

    public string? Note { get; init; }
}

/// <summary>A methodology version and its weights, read from <c>frameworks/sprs/</c>.</summary>
public sealed record SprsMethodology(string Name, string Version, int Start, IReadOnlyList<SprsWeight> Weights);

/// <summary>One audit row: the CMMC practice (such as <c>AC.L2-3.1.3</c>) and its result in CISO Assistant.</summary>
public sealed record SprsRow(string Practice, ComplianceResult Result);

/// <summary>Points taken off for one requirement, with the practice and result behind it (null when the audit has no row for it).</summary>
public sealed record SprsDeduction(string Requirement, string? Practice, ComplianceResult? Result, int Points, string? Note);

/// <summary>The score, each deduction, and whether the system security plan (3.12.4) is missing, which stops an assessment.</summary>
public sealed record SprsScore(int Score, IReadOnlyList<SprsDeduction> Deductions, bool SspMissing);

/// <summary>s17-05: the NIST SP 800-171 DoD Assessment Methodology score of a CMMC Level 2 audit. Pure.</summary>
public static class SprsScoring
{
    public static SprsScore Score(SprsMethodology methodology, IReadOnlyList<SprsRow> rows) => new(0, [], false);
}
