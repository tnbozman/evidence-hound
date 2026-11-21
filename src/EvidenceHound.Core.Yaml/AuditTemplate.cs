using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml;

/// <summary>Writes a starting audit file: every in-group requirement, titled in a comment, to do and not assessed.</summary>
public static class AuditTemplate
{
    public static string Write(FrameworkSnapshot snapshot, IReadOnlyList<string> groups, string refId) =>
        throw new NotImplementedException($"{snapshot}{groups}{refId}");
}
