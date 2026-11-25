using System.CommandLine;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// The <c>hound library</c> commands (s15-01): check a custom library, such as a risk matrix or threat catalogue, offline, and snapshot its
/// matrices' level names and its threats so risk studies are checked without CISO Assistant.
/// </summary>
internal static class LibraryCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.library");
        var library = new Argument<FileInfo>("library") { Description = "Path to a CISO Assistant library file (YAML), such as libraries/example-risk.yaml." };
        var validate = new Command("validate", "Check a custom library's risk matrices: a matrix URN, levels numbered from 0 without gaps, and one grid row per probability with one risk level per impact.") { library };
        validate.SetAction((parseResult, cancellationToken) => ValidateAsync(parseResult.GetRequiredValue(library), output, logger, cancellationToken));

        var outFile = new Option<FileInfo?>("--out") { Description = "Write the snapshot to this file instead of printing it, for example libraries/example-risk.json." };
        var snapshot = new Command("snapshot", "Make a library snapshot (risk matrix level names, threats and the libraries it brings with it) from a library file.") { library, outFile };
        snapshot.SetAction((parseResult, cancellationToken) =>
            SnapshotAsync(parseResult.GetRequiredValue(library), parseResult.GetValue(outFile), output, logger, cancellationToken));

        return new Command("library", "Check and snapshot custom libraries, such as risk matrices and threat catalogues.") { validate, snapshot };
    }

    private static async Task<int> ValidateAsync(FileInfo library, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        if (!library.Exists)
        {
            return Log.Problems(logger, [NotFound(library)]);
        }

        var yaml = await File.ReadAllTextAsync(library.FullName, cancellationToken);
        var problems = RiskLibraryReader.Validate(yaml, library.ToString());
        if (problems.Count > 0)
        {
            return Log.Problems(logger, problems);
        }

        return await RiskLibraryReader.Snapshot(yaml, library.ToString()).Match(
            async found =>
            {
                await output.WriteLineAsync($"valid: {Holds(found)}");
                return ExitCodes.Ok;
            },
            ps => Task.FromResult(Log.Problems(logger, ps)));
    }

    private static async Task<int> SnapshotAsync(FileInfo library, FileInfo? outFile, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var made = library.Exists
            ? RiskLibraryReader.Snapshot(await File.ReadAllTextAsync(library.FullName, cancellationToken), library.ToString())
            : Result.Fail<LibrarySnapshot>(NotFound(library));
        return await made.Match(
            async snapshot =>
            {
                var json = LibrarySnapshotJson.Write(snapshot);
                if (outFile is null)
                {
                    await output.WriteAsync(json);
                    return ExitCodes.Ok;
                }

                await File.WriteAllTextAsync(outFile.FullName, json, cancellationToken);
                await output.WriteLineAsync($"wrote {outFile}: {snapshot.LibraryUrn} version {snapshot.Version}, {Holds(snapshot)}");
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static Problem NotFound(FileInfo library) => new("library.file_not_found", $"Library file not found: {library}");

    /// <summary>What a library holds, such as "1 risk matrix, 2 threats".</summary>
    private static string Holds(LibrarySnapshot snapshot) =>
        $"{Count(snapshot.Matrices.Count, "risk matrix", "risk matrices")}, {Count(snapshot.Threats.Count, "threat", "threats")}";

    private static string Count(int count, string one, string many) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
}
