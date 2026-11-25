using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Risk;

/// <summary>Checks a custom library against the version CISO Assistant holds. Pure.</summary>
public static class LibraryValidation
{
    /// <summary>
    /// <c>library.version_not_bumped</c> when the file's objects differ from the stored library's but its version is not higher: CISO Assistant
    /// only re-publishes a library when its version goes up, so the edit would never arrive. Null when nothing is held or a hash is unknown.
    /// </summary>
    public static Problem? VersionBump(LibraryVersion declared, LibraryVersion? held)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return held is { Content: { } heldContent } && declared.Content is { } content && content != heldContent && declared.Version <= held.Version
            ? new Problem(
                "library.version_not_bumped",
                $"library '{declared.Urn}' changed but its version is still {declared.Version} (CISO Assistant holds version {held.Version}). " +
                $"Raise 'version' to version {held.Version + 1}: CISO Assistant only re-publishes a library when its version goes up.")
            : null;
    }

    /// <summary>
    /// Each custom library (a declared framework or library with a file) whose file differs from the one CISO Assistant stores at the same or a
    /// higher version, read from the held object's <c>version</c> and <c>content</c> (the digest of its stored objects) fields. A plan or apply stops on these.
    /// </summary>
    public static IReadOnlyList<ScopeProblem> Refused(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> held)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(held);
        var stored = held.Where(h => h.Held.Kind == ScopeKind.Framework).GroupBy(h => h.Held.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Held.Fields, StringComparer.Ordinal);
        return
        [
            .. from library in declared
               where library.Kind == ScopeKind.Framework && library.Files.Count > 0 && stored.ContainsKey(library.Key)
               let file = library.Files[0]
               let heldFields = stored[library.Key]
               let problem = VersionBump(
                   new LibraryVersion(library.Key, Number(library.Fields.GetValueOrDefault("version")), file.Content),
                   new LibraryVersion(library.Key, Number(heldFields.GetValueOrDefault("version")), heldFields.GetValueOrDefault("content")))
               where problem is not null
               select new ScopeProblem(problem.Code, new SourceLine(file.Path, 1), problem.Message, "Raise 'version' at the top of the library file, then plan again."),
        ];
    }

    private static int Number(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0;
}
