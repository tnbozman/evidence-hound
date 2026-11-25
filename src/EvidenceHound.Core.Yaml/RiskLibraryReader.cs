using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads a CISO Assistant library file's risk matrices and threats.</summary>
public static class RiskLibraryReader
{
    public static Result<LibrarySnapshot> Snapshot(string yaml, string source) => throw new NotImplementedException();

    public static IReadOnlyList<Problem> Validate(string yaml, string source) => throw new NotImplementedException();
}
