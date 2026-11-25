using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Risk;

/// <summary>Checks a custom risk matrix offline, before CISO Assistant sees it. Pure.</summary>
public static class RiskMatrixValidation
{
    public static IReadOnlyList<Problem> Check(RiskMatrix matrix, string packager) => throw new NotImplementedException();
}
