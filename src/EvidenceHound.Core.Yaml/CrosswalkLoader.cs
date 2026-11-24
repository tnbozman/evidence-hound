using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Yaml;

/// <summary>s12-08: parses a crosswalk file (see <c>schemas/crosswalk.schema.json</c>).</summary>
public static class CrosswalkLoader
{
    public static Result<CrosswalkDefinition> Parse(string yaml, string source) =>
        Result.Fail<CrosswalkDefinition>(new Problem("crosswalk.not_implemented", $"{source}: not implemented"));
}
