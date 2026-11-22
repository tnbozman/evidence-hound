using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

public enum DiffKind
{
    Same,
    Removed,
    Added,
}

/// <summary>One line of a diff: removed lines carry their library line number, added and unchanged lines their local one.</summary>
public sealed record DiffLine(DiffKind Kind, int Number, string Text)
{
    public override string ToString() => Kind switch
    {
        DiffKind.Removed => $"-{Number}: {Text}",
        DiffKind.Added => $"+{Number}: {Text}",
        _ => $" {Number}: {Text}",
    };
}

/// <summary>
/// <c>hound profile diff</c>: a line diff between a profile copied into the organisation's folder and the library file its
/// <c>source</c> names. Pure; the command reads the two files.
/// </summary>
public static class ProfileDiff
{
    /// <summary>The library's name as the first part of a <c>source</c>, as in <c>evidence-hound/profiles/example.yaml</c>.</summary>
    public const string LibraryName = "evidence-hound";

    /// <summary>A longest-common-subsequence line diff, ignoring line ending differences.</summary>
    public static IReadOnlyList<DiffLine> Compare(string library, string local)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(local);
        var a = Lines(library);
        var b = Lines(local);
        var common = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                common[i, j] = a[i] == b[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var lines = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                lines.Add(new(DiffKind.Same, y + 1, b[y]));
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || common[x, y + 1] >= common[x + 1, y]))
            {
                lines.Add(new(DiffKind.Added, y + 1, b[y]));
                y++;
            }
            else
            {
                lines.Add(new(DiffKind.Removed, x + 1, a[x]));
                x++;
            }
        }

        // Show a removed line before the line that replaced it.
        return [.. Reorder(lines)];
    }

    /// <summary>The library file a <c>source</c> names, which must stay inside <paramref name="libraryRoot"/>.</summary>
    public static Result<string> LibraryFile(string libraryRoot, string source)
    {
        ArgumentNullException.ThrowIfNull(libraryRoot);
        ArgumentNullException.ThrowIfNull(source);
        var parts = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var relative = parts is [LibraryName, .. var rest] ? rest : parts;
        return relative.Length == 0 || Path.IsPathRooted(source) || relative.Any(p => p is "." or ".." || p.Contains('\\', StringComparison.Ordinal))
            ? Result.Fail<string>(new Problem("profile.source_invalid", $"source '{source}' must name a file in the profile library, such as {LibraryName}/profiles/example.yaml."))
            : Result.Ok(Path.Combine([libraryRoot, .. relative]));
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private static IEnumerable<DiffLine> Reorder(List<DiffLine> lines)
    {
        var pending = new List<DiffLine>();
        foreach (var line in lines)
        {
            if (line.Kind == DiffKind.Same)
            {
                foreach (var change in pending.OrderBy(c => c.Kind == DiffKind.Added))
                {
                    yield return change;
                }

                pending.Clear();
                yield return line;
            }
            else
            {
                pending.Add(line);
            }
        }

        foreach (var change in pending.OrderBy(c => c.Kind == DiffKind.Added))
        {
            yield return change;
        }
    }
}
