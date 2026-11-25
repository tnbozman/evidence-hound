using System.CommandLine;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// s14-01: <c>hound mapping derive</c>: drafts a mapping for a new framework (such as the ASD Essentials) from an existing mapping (such as
/// the Essential Eight one) and a CISO Assistant transition mapping library. Every row is written <c>draft: true</c>.
/// </summary>
internal static class MappingDeriveCommand
{
    public static Command Build(TextWriter output, ILogger logger)
    {
        var mapping = new Argument<FileInfo>("mapping") { Description = "The existing framework mapping file (YAML), such as mappings/essential-eight-2023.yaml." };
        var from = new Option<FileInfo>("--from") { Description = "The snapshot of the existing mapping's framework.", Required = true };
        var to = new Option<FileInfo>("--to") { Description = "The snapshot of the new framework.", Required = true };
        var links = new Option<FileInfo>("--links")
        {
            Description = "A CISO Assistant requirement mapping library from the existing framework to the new one, such as "
                + "frameworks/mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml.",
            Required = true,
        };
        var groups = new Option<string?>("--groups") { Description = "Only the new framework's rows in these implementation groups, comma separated (for example 1 for priority phase 1). Default: every row." };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the draft here instead of printing it. An existing file is kept unless --force is given." };
        var force = new Option<bool>("--force") { Description = "Replace an existing --out file (such as a reviewed mapping)." };
        var command = new Command(
            "derive",
            "Draft a mapping for a new framework: each row gets the union of the checks of the rows the transition mapping links to it, marked draft: true.")
        {
            mapping, from, to, links, groups, outFile, force,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetRequiredValue(mapping),
            parseResult.GetRequiredValue(from),
            parseResult.GetRequiredValue(to),
            parseResult.GetRequiredValue(links),
            parseResult.GetValue(groups),
            parseResult.GetValue(outFile),
            parseResult.GetValue(force),
            output,
            logger,
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        FileInfo mappingFile, FileInfo fromFile, FileInfo toFile, FileInfo linksFile, string? groupList, FileInfo? outFile, bool force, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        if (outFile is { Exists: true } && !force)
        {
            Log.Usage(logger, $"{outFile} already exists, and may be a reviewed mapping. Write the draft to a new file, or add --force to replace it.");
            return ExitCodes.Usage;
        }

        var loaded = await MappingCommands.LoadAsync(mappingFile, cancellationToken);
        var source = await FrameworkCommands.LoadSnapshotAsync(fromFile, cancellationToken);
        var target = await FrameworkCommands.LoadSnapshotAsync(toFile, cancellationToken);
        var library = linksFile.Exists
            ? MappingLibraryReader.Read(await File.ReadAllTextAsync(linksFile.FullName, cancellationToken), linksFile.ToString())
            : Result.Fail<MappingLibrary>(new Problem("mapping_library.file_not_found", $"Mapping library not found: {linksFile}"));
        var groups = groupList is null ? [] : groupList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var derived = loaded.Bind(l => source.Bind(s => target.Bind(t => library.Bind(lib => MappingDerivation.Derive(l.Mapping, s, t, lib, groups)))));
        return await derived.Match(
            async draft =>
            {
                var yaml = DerivedMappingWriter.Write(draft, Path.GetFileName(linksFile.FullName));
                if (outFile is null)
                {
                    await output.WriteAsync(yaml);
                    return ExitCodes.Ok;
                }

                await File.WriteAllTextAsync(outFile.FullName, yaml, cancellationToken);
                var rows = draft.Requirements.Count(r => r.Checks.Count > 0);
                await output.WriteLineAsync(
                    $"wrote {rows} draft rows to {outFile}; {draft.Requirements.Count - rows} rows got no checks and are listed at its end. "
                    + "Runs publish no draft row: review each one, then delete its 'draft: true' line.");
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }
}
