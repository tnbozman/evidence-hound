using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Risk;

/// <summary>Checks a custom library against the version CISO Assistant holds. Pure.</summary>
public static class LibraryValidation
{
    /// <summary>
    /// <c>library.version_not_bumped</c> when the file's content differs from the stored library's but its version is not higher: CISO Assistant
    /// only re-publishes a library when its version goes up, so the edit would never arrive. Null when nothing is held or a hash is unknown.
    /// </summary>
    public static Problem? VersionBump(LibraryVersion declared, LibraryVersion? held)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return held is { Sha256: { } heldHash } && declared.Sha256 is { } hash && hash != heldHash && declared.Version <= held.Version
            ? new Problem(
                "library.version_not_bumped",
                $"library '{declared.Urn}' changed but its version is still {declared.Version} (CISO Assistant holds version {held.Version}). " +
                $"Raise 'version' to version {held.Version + 1}: CISO Assistant only re-publishes a library when its version goes up.")
            : null;
    }

    public static IReadOnlyList<ScopeProblem> Refused(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> held) => throw new NotImplementedException();
}
