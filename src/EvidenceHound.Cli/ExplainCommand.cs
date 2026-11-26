using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Explain;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound explain</c>: a plain-English review summary of a saved plan, a diff (a file or standard input) or two versions of a file. The
/// counts and flags are read in code; no model is asked. Review only: it never edits config or applies anything, and it is never part of
/// collection.
/// </summary>
internal static class ExplainCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.explain");
        var files = new Argument<FileInfo[]>("files") { Description = "A saved plan ('hound scope plan' or 'hound plan' output) or a diff; or two versions of one file. None reads a plan or diff from standard input.", Arity = new ArgumentArity(0, 2) };
        var repo = CommonOptions.Repo("The compliance repo, whose mappings the change is checked against. Default: the current folder.");
        var json = CommonOptions.Json("What code reads from the change (counts, objects, flags and topics) as JSON, for an agent.");
        var command = new Command("explain", "Explain a plan or config change in plain English for review, and flag what needs a careful look. Never changes anything.")
        {
            files, repo, json,
        };
        var retired = Retired.On(command, config: true, "--framework", "--skill");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await retired.NoteAsync(parseResult, terminal, cancellationToken);
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var read = await ReadAsync(parseResult.GetValue(files) ?? [], root, terminal, cancellationToken);
            if (read is Result<ChangeFacts>.Fail { Errors: var readProblems })
            {
                foreach (var problem in readProblems)
                {
                    Log.Usage(logger, problem.Message);
                }

                return ExitCodes.Usage;
            }

            var facts = ((Result<ChangeFacts>.Ok)read).Value;
            if (parseResult.GetValue(json))
            {
                await terminal.Output.WriteLineAsync(Json(facts).ToJsonString(CommandJson.Readable).AsMemory(), cancellationToken);
                return ExitCodes.Ok;
            }

            await terminal.Output.WriteAsync(Explanation.Render(facts).AsMemory(), cancellationToken);
            return ExitCodes.Ok;
        });
        return command;
    }

    /// <summary>The facts code read from the change, for an agent's review: nothing here comes from a model.</summary>
    private static System.Text.Json.Nodes.JsonObject Json(ChangeFacts facts) => new()
    {
        ["source"] = facts.Source,
        ["kind"] = facts.Kind,
        ["summary"] = facts.CountsText,
        ["counts"] = new System.Text.Json.Nodes.JsonArray([.. facts.Counts.Select(c => new System.Text.Json.Nodes.JsonObject { ["label"] = c.Label, ["value"] = c.Value })]),
        ["objects"] = new System.Text.Json.Nodes.JsonArray([.. facts.Objects.Select(o => System.Text.Json.Nodes.JsonValue.Create(o))]),
        ["flags"] = new System.Text.Json.Nodes.JsonArray([.. facts.Flags.Select(f => System.Text.Json.Nodes.JsonValue.Create(f))]),
        ["topics"] = new System.Text.Json.Nodes.JsonArray([.. facts.Topics.Select(t => System.Text.Json.Nodes.JsonValue.Create(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(t.ToString())))]),
    };

    private static async Task<Result<ChangeFacts>> ReadAsync(IReadOnlyList<FileInfo> files, string root, Terminal terminal, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, "mappings");
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [])
        {
            mappings[$"mappings/{Path.GetFileName(mapping)}"] = await File.ReadAllTextAsync(mapping, cancellationToken);
        }

        switch (files)
        {
            case []:
                var input = await terminal.Input.ReadToEndAsync(cancellationToken);
                return input.Trim().Length == 0
                    ? Result.Fail<ChangeFacts>(new Problem("explain.nothing", "Nothing to explain: name a saved plan or diff, two versions of a file, or pipe one in, such as 'git diff | hound explain'."))
                    : ChangeReader.Read(input, "standard input", mappings);
            case [var one]:
                return !one.Exists
                    ? Missing(one)
                    : ChangeReader.Read(await File.ReadAllTextAsync(one.FullName, cancellationToken), one.Name, mappings);
            case [var before, var after]:
                if (!before.Exists || !after.Exists)
                {
                    return Missing(before.Exists ? after : before);
                }

                var name = Path.GetRelativePath(root, after.FullName).Replace('\\', '/');
                var diff = ChangeReader.Diff(await File.ReadAllTextAsync(before.FullName, cancellationToken), await File.ReadAllTextAsync(after.FullName, cancellationToken), name.StartsWith("..", StringComparison.Ordinal) ? after.Name : name);
                return ChangeReader.Read(diff, $"{before.Name} to {after.Name}", mappings);
            default:
                return Result.Fail<ChangeFacts>(new Problem("explain.too_many", "Give one plan or diff, or two versions of one file."));
        }
    }

    private static Result<ChangeFacts> Missing(FileInfo file) => Result.Fail<ChangeFacts>(new Problem("explain.missing", $"{file.FullName} does not exist."));
}
