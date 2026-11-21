using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli;

/// <summary>The <c>hound mapping</c> commands: work with framework mapping files.</summary>
internal static class MappingCommands
{
    public static Command Build(TextWriter output, TextWriter error)
    {
        var file = new Argument<FileInfo>("file") { Description = "Path to a framework mapping file (YAML)." };
        var validate = new Command("validate", "Check a mapping file and report its problems with line numbers.") { file };
        validate.SetAction((parseResult, cancellationToken) =>
            ValidateAsync(parseResult.GetRequiredValue(file), output, error, cancellationToken));

        return new Command("mapping", "Work with framework mapping files.") { validate };
    }

    private static async Task<int> ValidateAsync(FileInfo file, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var load = await LoadAsync(file, cancellationToken);
        return await load.Match(
            async loaded =>
            {
                await output.WriteLineAsync($"valid: {loaded.Mapping.Requirements.Count} requirements");
                return ExitCodes.Ok;
            },
            async problems =>
            {
                foreach (var problem in problems)
                {
                    await error.WriteLineAsync($"error: {problem.Message}");
                }

                return ExitCodes.Configuration;
            });
    }

    internal static async Task<Result<LoadedMapping>> LoadAsync(FileInfo file, CancellationToken cancellationToken) =>
        file.Exists
            ? MappingLoader.Parse(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.ToString())
            : Result.Fail<LoadedMapping>(new Problem("mapping.file_not_found", $"Mapping file not found: {file}"));
}
