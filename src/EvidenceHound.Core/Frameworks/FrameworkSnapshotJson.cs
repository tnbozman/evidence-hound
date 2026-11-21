using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Frameworks;

/// <summary>Reads and writes a framework snapshot file (<c>frameworks/&lt;library&gt;.json</c>).</summary>
public static class FrameworkSnapshotJson
{
    public static string Write(FrameworkSnapshot snapshot) => throw new NotImplementedException();

    public static Result<FrameworkSnapshot> Read(string json, string source) => throw new NotImplementedException();
}
