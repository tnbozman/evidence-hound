using EvidenceHound.Core.Sprs;

namespace EvidenceHound.Core.Scope;

/// <summary>Which requirements CMMC allows on a POA&amp;M (s17-03).</summary>
public static class CmmcPoam
{
    public static string? Reason(string practice, SprsMethodology methodology) => null;

    public static IReadOnlyDictionary<string, string> For(IEnumerable<SspRequirement> rows, SprsMethodology methodology) => new Dictionary<string, string>();
}
