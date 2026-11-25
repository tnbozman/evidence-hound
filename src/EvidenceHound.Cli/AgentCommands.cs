using System.CommandLine;
using System.Reflection;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Onboarding.Agents;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound agent setup</c>: writes the files a coding assistant reads in the compliance repo (Claude Code, GitHub Copilot in VS Code, or
/// both) from hound's one source of skills. Files hound wrote carry its marker; a re-run replaces those and leaves every other file alone.
/// </summary>
internal static class AgentCommands
{
    public const string Question = "Which assistant do you use in VS Code (claude, copilot or both)";

    public static Command Build(Terminal terminal, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.agent");
        var assistant = new Option<string?>("--assistant") { Description = "The assistant to set up: claude, copilot or both. Asked when not given." };
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to write into. Default: the current folder." };
        var setup = new Command("setup", "Write the skills, agents and instructions your coding assistant reads in this repo. Re-running updates hound's files and keeps yours.")
        {
            assistant, repo,
        };
        setup.SetAction(async (parseResult, cancellationToken) =>
        {
            var answer = await new Wizard(terminal, yes: false).AskAsync(Question, parseResult.GetValue(assistant), "both", cancellationToken);
            if (Parse(answer) is not { } chosen)
            {
                Log.Usage(logger, $"'{answer}' is not an assistant hound sets up: use claude, copilot or both.");
                return ExitCodes.Usage;
            }

            return await SetupAsync(parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory(), chosen, parseResult.RootCommandResult.Command, terminal, logger, cancellationToken);
        });

        return new Command("agent", "Set up a coding assistant (Claude Code or GitHub Copilot) to work in the config repo with hound.") { setup };
    }

    public static Assistant? Parse(string answer) => Enum.TryParse<Assistant>(answer, ignoreCase: true, out var assistant) && Enum.IsDefined(assistant) && !int.TryParse(answer, out _)
        ? assistant
        : null;

    /// <summary>Writes the assistant's files into <paramref name="repo"/> and says, on the output, what happened to each.</summary>
    public static Task<int> SetupAsync(string repo, Assistant assistant, Command root, Terminal terminal, ILogger logger, CancellationToken cancellationToken) =>
        AgentKit.Files(assistant, Version(), Reference(root)).Match(
            files => WriteAsync(repo, files, terminal, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));

    private static async Task<int> WriteAsync(string repo, AgentFiles files, Terminal terminal, CancellationToken cancellationToken)
    {
        var kept = 0;
        foreach (var file in files.Write)
        {
            var path = Path.Combine(repo, file.Path);
            var held = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
            var outcome = held is null ? "Wrote" : held == file.Text ? "Unchanged" : AgentKit.WrittenByHound(held) ? "Updated" : "Kept";
            if (outcome is "Wrote" or "Updated")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Text, cancellationToken);
            }

            kept += outcome == "Kept" ? 1 : 0;
            await terminal.Output.WriteLineAsync($"{outcome} {file.Path}{(outcome == "Kept" ? " (yours: it has no hound marker)" : "")}".AsMemory(), cancellationToken);
        }

        foreach (var retired in files.Retire)
        {
            var path = Path.Combine(repo, retired);
            if (File.Exists(path) && AgentKit.WrittenByHound(await File.ReadAllTextAsync(path, cancellationToken)))
            {
                File.Delete(path);
                RemoveEmpty(Path.GetDirectoryName(path)!, repo);
                await terminal.Output.WriteLineAsync($"Removed {retired} (the same skill is in .claude/skills/, which Copilot reads too)".AsMemory(), cancellationToken);
            }
        }

        if (kept > 0)
        {
            await terminal.Output.WriteLineAsync("A kept file is your own. To have hound write its version, delete the file and run hound agent setup again.".AsMemory(), cancellationToken);
        }

        await terminal.Output.WriteLineAsync("Review and commit these files. Then ask your assistant to onboard a system with hound; you approve with hound approve in a terminal.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    /// <summary>Every visible command under <paramref name="root"/>, depth first, with its usage, description and options (s15-13).</summary>
    internal static IReadOnlyList<CommandEntry> Reference(Command root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var entries = new List<CommandEntry>();
        void Walk(Command command, string path)
        {
            foreach (var sub in command.Subcommands.Where(c => !c.Hidden))
            {
                var at = $"{path} {sub.Name}";
                var arguments = sub.Arguments.Where(a => !a.Hidden).Select(a => a.Arity.MinimumNumberOfValues == 0 ? $"[<{a.Name}>]" : $"<{a.Name}>");
                entries.Add(new CommandEntry(
                    string.Join(' ', [at, .. arguments]),
                    sub.Description ?? "",
                    [.. sub.Options.Where(o => !o.Hidden && o.Name is not ("--help" or "--version")).Select(o => new CommandOption(o.Name, o.Description ?? ""))]));
                Walk(sub, at);
            }
        }

        Walk(root, "hound");
        return entries;
    }

    /// <summary>Removes <paramref name="folder"/> and its parents while they are empty, stopping at <paramref name="repo"/>.</summary>
    private static void RemoveEmpty(string folder, string repo)
    {
        for (var current = new DirectoryInfo(folder); current is { Exists: true } && current.FullName.Length > Path.GetFullPath(repo).Length && !current.EnumerateFileSystemInfos().Any(); current = current.Parent)
        {
            current.Delete();
        }
    }

    private static string Version() =>
        (typeof(AgentCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
