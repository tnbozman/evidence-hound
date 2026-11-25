using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Frameworks;

/// <summary>Reads and writes a library snapshot file (<c>libraries/&lt;library&gt;.json</c>).</summary>
public static class LibrarySnapshotJson
{
    public static string Write(LibrarySnapshot snapshot) => throw new NotImplementedException();

    public static Result<LibrarySnapshot> Read(string json, string source) => throw new NotImplementedException();
}
