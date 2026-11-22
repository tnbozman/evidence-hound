using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

public enum DiffKind
{
    Same,
    Removed,
    Added,
}

public sealed record DiffLine(DiffKind Kind, int Number, string Text);

public static class ProfileDiff
{
    public static IReadOnlyList<DiffLine> Compare(string library, string local) => throw new NotImplementedException();

    public static Result<string> LibraryFile(string libraryRoot, string source) => throw new NotImplementedException();
}
