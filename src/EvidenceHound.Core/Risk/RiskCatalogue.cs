using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Risk;

/// <summary>The risk matrices and threats the scope's library snapshots hold: a matrix by URN, a threat by URN or ref_id. Pure.</summary>
public sealed class RiskCatalogue
{
    private readonly Dictionary<string, MatrixSnapshot> _matrices;
    private readonly Dictionary<string, Threat> _threats;

    private RiskCatalogue(Dictionary<string, MatrixSnapshot> matrices, Dictionary<string, Threat> threats) =>
        (_matrices, _threats) = (matrices, threats);

    public static RiskCatalogue Of(IEnumerable<LibrarySnapshot> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var list = libraries.ToList();
        var threats = new Dictionary<string, Threat>(StringComparer.Ordinal);
        foreach (var threat in list.SelectMany(l => l.Threats))
        {
            threats.TryAdd(threat.Urn, threat);
            if (threat.RefId is { } refId)
            {
                threats.TryAdd(refId, threat);
            }
        }

        return new(list.SelectMany(l => l.Matrices).GroupBy(m => m.Urn, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal), threats);
    }

    public MatrixSnapshot? Matrix(string urn) => _matrices.GetValueOrDefault(urn);

    /// <summary>The threat a scenario names, by URN or ref_id; null when no snapshot holds it.</summary>
    public Threat? Threat(string urnOrRef) => _threats.GetValueOrDefault(urnOrRef);
}
