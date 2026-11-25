using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Risk;

/// <summary>Checks a custom library against the version CISO Assistant holds. Pure.</summary>
public static class LibraryValidation
{
    public static Problem? VersionBump(LibraryVersion declared, LibraryVersion? held) => throw new NotImplementedException();
}
