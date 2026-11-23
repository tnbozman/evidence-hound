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

        var deny = new Option<FileInfo?>("--deny-terms") { Description = "A file of terms, one per line, that must never reach a model; a skill holding one is a warning." };
        var validate = new Command("validate", "Check every skill in the folder: frontmatter, steps, size, overrides, and anything that looks like organisation data.") { FolderOption(), deny };
        validate.SetAction((parseResult, cancellationToken) =>
            ValidateAsync(output, logger, Folder(parseResult, configuration), parseResult.GetValue(deny)?.FullName, cancellationToken));

        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo the drafts read: its systems, suppliers and capability map. Default: the current folder." };
        var only = new Option<string[]>("--only") { Description = $"Draft only this skill: {string.Join(", ", OrganisationSkills.Kinds.Select(k => k.Name))}. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var noModel = new Option<bool>("--no-model") { Description = "Write the skills as templates, their questions as headings, to fill in by hand." };
        var show = new Option<bool>("--show-requests") { Description = "Show each request before it is sent to the model." };
        var config = new Option<FileInfo>("--config") { Description = $"The onboarding config. Default: {OnboardCommands.DefaultConfig} in the repo." };
        var draft = new Command("draft", "Draft the organisation's own skills (its profile, risk appetite, IT and AI landscape) into the skills folder, for you to review, edit and commit.")
        {
            FolderOption(), repo, only, noModel, show, config,
        };
        draft.SetAction((parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            return SkillDraftCommand.RunAsync(
                new SkillDraftCommand.Settings(
                    Folder(parseResult, configuration),
                    root,
                    parseResult.GetValue(only) ?? [],
                    parseResult.GetValue(noModel),
                    parseResult.GetValue(show),
                    parseResult.GetValue(config)?.FullName ?? Path.Combine(root, OnboardCommands.DefaultConfig)),
                terminal,
                logger,
                configuration,
                cancellationToken);
        });

        return new Command("skill", "Work with the onboarding assistant's skills.") { list, create, add, validate, draft };
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
        foreach (var warning in SkillCatalogue.Warnings(added, []))
        {
            await output.WriteLineAsync($"warning: {warning.Message}".AsMemory(), cancellationToken);
        }

        await output.WriteLineAsync($"Added {added.Name} {added.Version} to {folder}{(added.Overrides is { } o ? $", replacing the default {o}" : "")}.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    private static async Task<int> ValidateAsync(TextWriter output, ILogger logger, string folder, string? denyTermsFile, CancellationToken cancellationToken)
    {
        var catalogue = Catalogue(folder);
        if (catalogue is not Result<SkillCatalogue>.Ok { Value: var valid })
        {
            return Log.Problems(logger, ((Result<SkillCatalogue>.Fail)catalogue).Errors);
        }

        IReadOnlyList<string> denyTerms = denyTermsFile is null ? [] : [.. (await File.ReadAllLinesAsync(denyTermsFile, cancellationToken)).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))];
        var warnings = valid.Skills.SelectMany(s => SkillCatalogue.Warnings(s, denyTerms)).ToArray();
        foreach (var warning in warnings)
        {
            await output.WriteLineAsync($"error {warning.Code}: {warning.Message}".AsMemory(), cancellationToken);
        }

        var mine = valid.Skills.Count(s => s.Source == SkillSource.Organisation);
        await output.WriteLineAsync($"{valid.Skills.Count} skills valid ({mine} from {folder}), {warnings.Length} warnings.".AsMemory(), cancellationToken);

        // Skills are sent to the model provider: one that holds organisation data is refused, not only reported.
        return warnings.Length == 0 ? ExitCodes.Ok : ExitCodes.Configuration;
    }
}
