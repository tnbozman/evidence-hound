using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Yaml;

/// <summary>Checks a loaded mapping against a framework snapshot, offline. Every problem names the file and line.</summary>
public static class MappingValidator
{
    public static IReadOnlyList<Problem> Validate(LoadedMapping loaded, FrameworkSnapshot snapshot) => throw new NotImplementedException();
}
