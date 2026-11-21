using System.CommandLine;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound mapping</c> commands: work with framework mapping files.</summary>
internal static class MappingCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.mapping");
        var file = new Argument<FileInfo>("file") { Description = "Path to a framework mapping file (YAML)." };
        var framework = new Option<FileInfo?>("--framework")
        {
            Description = "A framework snapshot (from 'hound framework snapshot'): also report unknown URNs and refs, and refs that match several rows.",
        };
        var validate = new Command("validate", "Check a mapping file and report its problems with line numbers.") { file, framework };
        validate.SetAction((parseResult, cancellationToken) =>
            ValidateAsync(parseResult.GetRequiredValue(file), parseResult.GetValue(framework), output, logger, cancellationToken));

        var mappingFile = new Argument<FileInfo>("mapping") { Description = "Path to a framework mapping file (YAML)." };
        var checksFile = new Argument<FileInfo>("checks") { Description = "Path to a check results file (JSON)." };
        var apply = new Command("apply", "Work out requirement results from check results and print them as a table.") { mappingFile, checksFile };
        apply.SetAction((parseResult, cancellationToken) =>
            ApplyAsync(parseResult.GetRequiredValue(mappingFile), parseResult.GetRequiredValue(checksFile), output, logger, cancellationToken));

        return new Command("mapping", "Work with framework mapping files.") { validate, apply };
    }

    private static async Task<int> ValidateAsync(FileInfo file, FileInfo? framework, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var load = await LoadAsync(file, cancellationToken);
        if (framework is not null)
        {
            var snapshot = await FrameworkCommands.LoadSnapshotAsync(framework, cancellationToken);
            load = load.Bind(loaded => snapshot.Bind(s => MappingValidator.Validate(loaded, s) switch
            {
                [] => Result.Ok(loaded),
                var problems => Result.Fail<LoadedMapping>(problems),
            }));
        }

        return await load.Match(
            async loaded =>
            {
                await output.WriteLineAsync($"valid: {loaded.Mapping.Requirements.Count} requirements");
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static async Task<int> ApplyAsync(FileInfo mappingFile, FileInfo checksFile, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var mapping = await LoadAsync(mappingFile, cancellationToken);
        var checks = (await CheckResultsJson.LoadRunAsync(checksFile, cancellationToken)).Map(run => run.Checks);
        var results = mapping.Bind(loaded => checks.Map(list => MappingEngine.ApplyMapping(loaded.Mapping, list)));
        return await results.Match(
            async rows =>
            {
                await output.WriteAsync(RequirementTable.Render(rows));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    internal static async Task<Result<LoadedMapping>> LoadAsync(FileInfo file, CancellationToken cancellationToken) =>
        file.Exists
            ? MappingLoader.Parse(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.ToString())
            : Result.Fail<LoadedMapping>(new Problem("mapping.file_not_found", $"Mapping file not found: {file}"));
}
