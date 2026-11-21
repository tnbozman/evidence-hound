using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound framework</c> commands: work with framework libraries and their snapshots.</summary>
internal static class FrameworkCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.framework");
        var library = new Argument<FileInfo>("library") { Description = "Path to a CISO Assistant library file (YAML)." };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the snapshot to this file instead of printing it, for example frameworks/essential-eight-2023.json." };
        var snapshot = new Command("snapshot", "Make a framework snapshot (URNs, ref_ids, groups and titles of every assessable requirement) from a library file.") { library, outFile };
        snapshot.SetAction((parseResult, cancellationToken) =>
            SnapshotAsync(parseResult.GetRequiredValue(library), parseResult.GetValue(outFile), output, logger, cancellationToken));

        return new Command("framework", "Work with framework libraries and snapshots.") { snapshot };
    }

    private static async Task<int> SnapshotAsync(FileInfo library, FileInfo? outFile, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var made = library.Exists
            ? FrameworkLibraryReader.Snapshot(await File.ReadAllTextAsync(library.FullName, cancellationToken), library.ToString())
            : Result.Fail<FrameworkSnapshot>(new Problem("framework.file_not_found", $"Library file not found: {library}"));
        return await made.Match(
            async snapshot =>
            {
                var json = FrameworkSnapshotJson.Write(snapshot);
                if (outFile is null)
                {
                    await output.WriteAsync(json);
                    return ExitCodes.Ok;
                }

                await File.WriteAllTextAsync(outFile.FullName, json, cancellationToken);
                await output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"wrote {outFile}: {snapshot.FrameworkUrn} version {snapshot.Version}, {snapshot.Nodes.Count} assessable requirements"));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>Reads a snapshot file written by <c>hound framework snapshot</c>.</summary>
    internal static async Task<Result<FrameworkSnapshot>> LoadSnapshotAsync(FileInfo file, CancellationToken cancellationToken) =>
        file.Exists
            ? FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.ToString())
            : Result.Fail<FrameworkSnapshot>(new Problem("framework.file_not_found", $"Framework snapshot not found: {file}"));
}
