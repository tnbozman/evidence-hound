using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Explain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound explain</c>: a plain-English review summary of a saved plan, a diff (a file or standard input) or two versions of a file. The
/// counts and flags are read in code; the model, through the onboarding gate, only adds a summary checked against them. Review only: it
/// never edits config or applies anything, and it is never part of collection.
/// </summary>
internal static class ExplainCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.explain");
        var files = new Argument<FileInfo[]>("files") { Description = "A saved plan ('hound scope plan' or 'hound plan' output) or a diff; or two versions of one file. None reads a plan or diff from standard input.", Arity = new ArgumentArity(0, 2) };
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo: its onboarding config, skills and request record. Default: the current folder." };
        var framework = new Option<string>("--framework") { Description = "The framework whose mapping skills apply, as skills name it. Default: essential-eight-2023." };
        var skills = new Option<string[]>("--skill") { Description = "Use this skill instead of the ones chosen by what the change touches. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var noModel = new Option<bool>("--no-model") { Description = "Only the counts and flags read in code; no model summary." };
        var show = new Option<bool>("--show-requests") { Description = "Show each request before it is sent to the model." };
        var config = new Option<FileInfo>("--config") { Description = $"The onboarding config. Default: {OnboardCommands.DefaultConfig} in the repo." };
        var json = new Option<bool>("--json") { Description = "What code reads from the change (counts, objects, flags and topics) as JSON, for an agent. Asks no model." };
        var command = new Command("explain", "Explain a plan or config change in plain English for review, and flag what needs a careful look. Never changes anything.")
        {
            files, repo, framework, skills, noModel, show, config, json,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
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
                await terminal.Output.WriteLineAsync(Json(facts).ToJsonString(OnboardAgentCommand.Indented).AsMemory(), cancellationToken);
                return ExitCodes.Ok;
            }

            if (parseResult.GetValue(noModel))
            {
                await terminal.Output.WriteAsync(Explanation.Render(facts, null, []).AsMemory(), cancellationToken);
                return ExitCodes.Ok;
            }

            var explained = await (await OnboardSystemCommand.AssistantAsync(
                    parseResult.GetValue(config)?.FullName ?? Path.Combine(root, OnboardCommands.DefaultConfig),
                    Path.Combine(root, "onboarding", "explain", "requests"),
                    parseResult.GetValue(show),
                    terminal,
                    configuration,
                    cancellationToken))
                .BindAsync(assistant => ExplainSkills.Choose(assistant.Skills, facts.Topics, parseResult.GetValue(framework) ?? "essential-eight-2023", parseResult.GetValue(skills) ?? [])
                    .BindAsync(async chosen =>
                    {
                        await assistant.Record.SaveSkillsAsync(chosen, cancellationToken);
                        return (await Explanation.DraftAsync(assistant.Model, chosen, facts, assistant.Terms, cancellationToken)).Map(reply => Explanation.Render(facts, reply, chosen));
                    }));
            return await explained.Match(
                async text =>
                {
                    await terminal.Output.WriteAsync(text.AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
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
