using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound skill</c>: the onboarding assistant's guidance, as default skills built into hound and the organisation's own in its skills folder.</summary>
internal static class SkillCommands
{
    public const string SkillsSetting = "HOUND_SKILLS_PATH";

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var output = terminal.Output;
        var logger = loggers.CreateLogger("hound.skill");

        var list = new Command("list", "List every active skill: its source (default or organisation), version, SHA-256, and any default it overrides.") { FolderOption() };
        list.SetAction((parseResult, cancellationToken) => ListAsync(output, logger, Folder(parseResult, configuration), cancellationToken));

        var name = new Argument<string>("name") { Description = "The new skill's name: lower case words joined by dashes." };
        var create = new Command("new", "Write a skeleton skill to fill in, in the skills folder.") { name, FolderOption() };
        create.SetAction((parseResult, cancellationToken) => NewAsync(output, logger, Folder(parseResult, configuration), parseResult.GetRequiredValue(name), cancellationToken));

        var file = new Argument<FileInfo>("file") { Description = "The skill file to check and copy into the skills folder." };
        var add = new Command("add", "Validate a skill against the defaults and the folder's skills, then copy it into the skills folder.") { file, FolderOption() };
        add.SetAction((parseResult, cancellationToken) => AddAsync(output, logger, Folder(parseResult, configuration), parseResult.GetRequiredValue(file).FullName, cancellationToken));

        var validate = new Command("validate", "Check every skill in the folder: frontmatter, steps, size and overrides.") { FolderOption() };
        var retiredValidate = Retired.On(validate, config: false, "--deny-terms");
        validate.SetAction(async (parseResult, cancellationToken) =>
        {
            await retiredValidate.NoteAsync(parseResult, terminal, cancellationToken);
            return await ValidateAsync(output, logger, Folder(parseResult, configuration), cancellationToken);
        });

        var only = new Option<string[]>("--only") { Description = $"Write only this skill: {string.Join(", ", OrganisationSkills.Kinds.Select(k => k.Name))}. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var draft = new Command("draft", "Write the organisation's own skills (its profile, risk appetite, IT and AI landscape) as templates into the skills folder, for you to fill in, review and commit.")
        {
            FolderOption(), only,
        };
        var retiredDraft = Retired.On(draft, config: true, "--repo");
        draft.SetAction(async (parseResult, cancellationToken) =>
        {
            await retiredDraft.NoteAsync(parseResult, terminal, cancellationToken);
            return await SkillDraftCommand.RunAsync(new SkillDraftCommand.Settings(Folder(parseResult, configuration), parseResult.GetValue(only) ?? []), terminal, logger, cancellationToken);
        });

        return new Command("skill", "Work with the skills that guide onboarding: hound's defaults and the organisation's own.") { list, create, add, validate, draft };
    }

    private static Option<DirectoryInfo> FolderOption() =>
        new("--skills") { Description = $"The organisation's skills folder. Default: {SkillsSetting}, else ./skills." };

    private static string Folder(ParseResult parseResult, IConfiguration configuration) =>
        parseResult.GetValue<DirectoryInfo>("--skills")?.FullName ?? configuration[SkillsSetting] ?? Path.GetFullPath("skills");

    private static Result<SkillCatalogue> Catalogue(string folder) =>
        SkillCatalogue.Defaults().Bind(defaults => SkillCatalogue.FromFolder(folder).Bind(mine => SkillCatalogue.Build(defaults, mine)));

    private static async Task<int> ListAsync(TextWriter output, ILogger logger, string folder, CancellationToken cancellationToken)
    {
        var loaded = Catalogue(folder);
        if (loaded is not Result<SkillCatalogue>.Ok { Value: var catalogue })
        {
            return Log.Problems(logger, ((Result<SkillCatalogue>.Fail)loaded).Errors);
        }

        var lines = new List<string> { "| Skill | Source | Version | SHA-256 | Steps | Overrides |", "| --- | --- | --- | --- | --- | --- |" };
        lines.AddRange(catalogue.Skills.Select(s =>
            $"| {s.Name} | {(s.Source == SkillSource.Default ? "default" : "organisation")} | {s.Version} | {s.Sha256[..12]} | {string.Join(", ", s.AppliesTo.Select(a => a.ToString().ToLowerInvariant()))} | {s.Overrides ?? ""} |"));
        await output.WriteLineAsync(string.Join(Environment.NewLine, lines).AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    private static async Task<int> NewAsync(TextWriter output, ILogger logger, string folder, string name, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, $"{name}.md");
        if (File.Exists(path))
        {
            return Log.Problems(logger, [new Problem("skill.exists", $"{path} already exists.")]);
        }

        var text = $"""
            ---
            name: {name}
            description: One sentence on what this guidance is for.
            version: 0.1.0
            applies_to: [applicability]
            # frameworks: [essential-eight-2023]
            # overrides: applicability
            ---
            # {name}

            Write the guidance the assistant should follow at the steps in applies_to (supplier, system, applicability, profile, mapping).
            Keep organisation data out: skills are sent to the model provider.

            """;
        if (SkillParser.Parse(text, path, SkillSource.Organisation) is Result<Skill>.Fail { Errors: var problems })
        {
            return Log.Problems(logger, problems);
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, text, cancellationToken);
        await output.WriteLineAsync($"Wrote {path}. Edit it, then 'hound skill add {path}'.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    private static async Task<int> AddAsync(TextWriter output, ILogger logger, string folder, string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return Log.Problems(logger, [new Problem("skill.file_not_found", $"{file} not found.")]);
        }

        var text = await File.ReadAllTextAsync(file, cancellationToken);
        var target = Path.Combine(folder, Path.GetFileName(file));
        var checkedSkill = SkillParser.Parse(text, file, SkillSource.Organisation)
            .Bind(skill => SkillCatalogue.Defaults().Bind(defaults => SkillCatalogue.FromFolder(folder)
                .Bind(mine => SkillCatalogue.Build(defaults, [.. mine.Where(m => m.Path != target), skill]))
                .Map(_ => skill)));
        if (checkedSkill is not Result<Skill>.Ok { Value: var added })
        {
            return Log.Problems(logger, ((Result<Skill>.Fail)checkedSkill).Errors);
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(target, text, cancellationToken);
        await output.WriteLineAsync($"Added {added.Name} {added.Version} to {folder}{(added.Overrides is { } o ? $", replacing the default {o}" : "")}.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    private static async Task<int> ValidateAsync(TextWriter output, ILogger logger, string folder, CancellationToken cancellationToken)
    {
        var catalogue = Catalogue(folder);
        if (catalogue is not Result<SkillCatalogue>.Ok { Value: var valid })
        {
            return Log.Problems(logger, ((Result<SkillCatalogue>.Fail)catalogue).Errors);
        }

        var mine = valid.Skills.Count(s => s.Source == SkillSource.Organisation);
        await output.WriteLineAsync($"{valid.Skills.Count} skills valid ({mine} from {folder}).".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }
}
