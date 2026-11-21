using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Publishing;

/// <summary>Names for evidence in CISO Assistant, which requires a name to be unique within a folder.</summary>
public static class EvidenceNames
{
    /// <summary>The evidence ID when it already ends with the file name, otherwise "file name (ID)". Unique because IDs are.</summary>
    public static string For(Evidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.Id.EndsWith(evidence.FileName, StringComparison.Ordinal) ? evidence.Id : $"{evidence.FileName} ({evidence.Id})";
    }
}
