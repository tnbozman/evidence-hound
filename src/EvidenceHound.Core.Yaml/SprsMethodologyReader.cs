using EvidenceHound.Core.Results;
using EvidenceHound.Core.Sprs;

namespace EvidenceHound.Core.Yaml;

/// <summary>s17-05: reads a methodology file from <c>frameworks/sprs/</c>. Every problem names the file and line.</summary>
public static class SprsMethodologyReader
{
    public static Result<SprsMethodology> Parse(string yaml, string source) =>
        Result.Fail<SprsMethodology>(new Problem("sprs.not_read", source));
}
