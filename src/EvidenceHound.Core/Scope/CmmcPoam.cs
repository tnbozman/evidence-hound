using System.Globalization;
using EvidenceHound.Core.Sprs;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Which requirements CMMC allows on a Level 2 POA&amp;M (s17-03), from 32 CFR 170.21(a)(2): (ii) only requirements worth 1 point in the
/// NIST SP 800-171 DoD Assessment Methodology, except SC.L2-3.13.11 when encryption is used but not FIPS-validated (3 points); (iii) never
/// AC.L2-3.1.20, AC.L2-3.1.22, CA.L2-3.12.4, PE.L2-3.10.3, PE.L2-3.10.4 or PE.L2-3.10.5. Practices are matched by their NIST SP 800-171
/// requirement number, so <c>PE.L1-3.10.3</c> counts as 3.10.3. Pure.
/// </summary>
public static class CmmcPoam
{
    public static readonly string Source = "32 CFR 170.21(a)(2)";

    /// <summary>The requirements 32 CFR 170.21(a)(2)(iii) never allows on a POA&amp;M.</summary>
    public static IReadOnlyList<string> NeverAllowed { get; } = ["3.1.20", "3.1.22", "3.10.3", "3.10.4", "3.10.5", "3.12.4"];

    private static readonly string Encryption = "3.13.11";

    /// <summary>Why CMMC does not allow the practice on a POA&amp;M; null when it may sit there.</summary>
    public static string? Reason(string practice, SprsMethodology methodology)
    {
        ArgumentNullException.ThrowIfNull(practice);
        ArgumentNullException.ThrowIfNull(methodology);
        var requirement = SprsScoring.Requirement(practice);
        if (NeverAllowed.Contains(requirement, StringComparer.Ordinal))
        {
            return "CMMC never allows this requirement on a POA&M (32 CFR 170.21(a)(2)(iii)); meet it before a Conditional Level 2 status.";
        }

        var points = methodology.Weights.FirstOrDefault(w => w.Requirement == requirement)?.Points;
        if (points is not > 1)
        {
            return null;
        }

        var worth = points.Value.ToString(CultureInfo.InvariantCulture);
        return requirement == Encryption
            ? $"worth {worth} points; CMMC allows it on a POA&M only when encryption is used but is not FIPS-validated (3 points, 32 CFR 170.21(a)(2)(ii)); a person decides."
            : $"worth {worth} points; CMMC allows only 1-point requirements on a POA&M (32 CFR 170.21(a)(2)(ii)); meet it before a Conditional Level 2 status.";
    }

    /// <summary>The unmet rows CMMC does not allow on a POA&amp;M, by requirement, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> For(IEnumerable<SspRequirement> rows, SprsMethodology methodology)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(methodology);
        return rows
            .Where(r => r.Ref.Value is not null && SspStatements.Unmet.Contains(r.Status.Value))
            .Select(r => (Ref: r.Ref.Value!, Reason: Reason(r.Ref.Value!, methodology)))
            .Where(r => r.Reason is not null)
            .DistinctBy(r => r.Ref, StringComparer.Ordinal)
            .ToDictionary(r => r.Ref, r => r.Reason!, StringComparer.Ordinal);
    }
}
