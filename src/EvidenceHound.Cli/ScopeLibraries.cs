using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli;

/// <summary>
/// The custom libraries a scope's setup lists with a file (s15-01), such as a risk matrix or threat catalogue: each file to upload with its
/// SHA-256, its snapshot (from its <c>snapshot</c> file, else made from the library), and any problem with its risk matrices.
/// </summary>
internal static class ScopeLibraries
{
    public sealed record Loaded(
        IReadOnlyDictionary<string, LibrarySnapshot> Snapshots, IReadOnlyDictionary<string, IReadOnlyList<ScopeFile>> Files, IReadOnlyList<ScopeProblem> Problems);

    /// <summary>Reads each library file and snapshot, relative to <paramref name="baseDir"/> (the folder that holds setup.yaml).</summary>
    public static async Task<Loaded> LoadAsync(IReadOnlyList<ScopeLibraryFile> libraries, string baseDir, CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, LibrarySnapshot>(StringComparer.Ordinal);
        var files = new Dictionary<string, IReadOnlyList<ScopeFile>>(StringComparer.Ordinal);
        var problems = new List<ScopeProblem>();
        foreach (var library in libraries)
        {
            var path = Path.GetFullPath(library.File, baseDir);
            if (!File.Exists(path))
            {
                problems.Add(new ScopeProblem("scope.library_file_missing", library.At, $"{library.File} does not exist.", "Add the library file, or fix the path: it is relative to scope/."));
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            files[library.Urn] = [new ScopeFile(path, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))];
            var yaml = System.Text.Encoding.UTF8.GetString(bytes);
            problems.AddRange(RiskLibraryReader.Validate(yaml, library.File).Select(p => At(p, library, "Fix the library file, then check it with 'hound library validate'.")));
            var snapshot = library.Snapshot is { } snapshotFile
                ? await SnapshotFileAsync(Path.GetFullPath(snapshotFile, baseDir), snapshotFile, cancellationToken)
                : RiskLibraryReader.Snapshot(yaml, library.File);
            snapshot.Match(
                found =>
                {
                    if (found.LibraryUrn == library.Urn)
                    {
                        snapshots[library.Urn] = found;
                    }
                    else
                    {
                        problems.Add(new ScopeProblem("scope.library_snapshot_mismatch", library.At, $"the snapshot is library '{found.LibraryUrn}', not '{library.Urn}'.", "Make it again with 'hound library snapshot <file>'."));
                    }

                    return 0;
                },
                ps =>
                {
                    problems.AddRange(ps.Select(p => At(p, library, "Make the snapshot again with 'hound library snapshot <file>'.")));
                    return 0;
                });
        }

        return new(snapshots, files, problems);
    }

    private static async Task<Result<LibrarySnapshot>> SnapshotFileAsync(string path, string source, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? LibrarySnapshotJson.Read(await File.ReadAllTextAsync(path, cancellationToken), source)
            : Result.Fail<LibrarySnapshot>(new Problem("scope.library_file_missing", $"{source} does not exist."));

    private static ScopeProblem At(Problem problem, ScopeLibraryFile library, string hint) => new(problem.Code, library.At, problem.Message, hint);
}
