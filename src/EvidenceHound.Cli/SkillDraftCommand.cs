using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound skill draft</c>: the organisation's own skills, drafted from the person's answers and the config repo through the onboarding
/// gate (or as templates with <c>--no-model</c>), written only into the organisation's skills folder for review.
/// </summary>
internal static class SkillDraftCommand
{
    public sealed record Settings(string Skills, string Repo, IReadOnlyList<string> Only, bool NoModel, bool Show, string Config);

    public static async Task<int> RunAsync(Settings settings, Terminal terminal, ILogger logger, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var unknown = settings.Only.Where(n => OrganisationSkills.Kinds.All(k => k.Name != n)).ToArray();
        if (unknown.Length > 0)
        {
            Log.Usage(logger, $"Not a skill hound drafts: {string.Join(", ", unknown)}. Choose from {string.Join(", ", OrganisationSkills.Kinds.Select(k => k.Name))}.");
            return ExitCodes.Usage;
        }

        var kinds = OrganisationSkills.Kinds.Where(k => settings.Only.Count == 0 || settings.Only.Contains(k.Name)).ToArray();
        OnboardSystemCommand.Assistant? assistant = null;
        if (!settings.NoModel)
        {
            var loaded = await OnboardSystemCommand.AssistantAsync(settings.Config, Path.Combine(settings.Repo, "onboarding", "skills", "requests"), settings.Show, terminal, configuration, cancellationToken);
            if (loaded is Result<OnboardSystemCommand.Assistant>.Fail { Errors: var problems })
            {
                return Log.Problems(logger, problems);
            }

            assistant = ((Result<OnboardSystemCommand.Assistant>.Ok)loaded).Value;
        }

        var material = assistant is null ? [] : OrganisationSkills.Material(await MaterialAsync(settings.Repo, cancellationToken), assistant.Terms);
        var drafts = new List<SkillDraft>();
        foreach (var kind in kinds)
        {
            var path = Path.Combine(settings.Skills, $"{kind.Name}.md");
            var existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
            Result<SkillDraft> drafted;
            if (assistant is null)
            {
                drafted = Result.Ok(OrganisationSkills.Template(kind, existing));
            }
            else
            {
                await terminal.Error.WriteLineAsync($"{kind.Name}: {kind.Description} Refer to roles, not people; leave out host names, addresses and IDs. Empty skips a question.".AsMemory(), cancellationToken);
                var answers = new List<SkillAnswer>();
                foreach (var question in kind.Questions)
                {
                    await terminal.Error.WriteAsync($"{question} ".AsMemory(), cancellationToken);
                    answers.Add(new SkillAnswer(question, await terminal.Input.ReadLineAsync(cancellationToken) ?? ""));
                }

                drafted = (await OrganisationSkills.DraftAsync(assistant.Model, kind, answers, material, assistant.Terms, cancellationToken))
                    .Map(body => OrganisationSkills.Draft(kind, body, existing, assistant.Terms));
            }

            var checkedDraft = drafted.Bind(d => SkillParser.Parse(d.Text, path, SkillSource.Organisation).Map(_ => d));
            if (checkedDraft is Result<SkillDraft>.Fail { Errors: var draftProblems })
            {
                return Log.Problems(logger, draftProblems);
            }

            var draft = ((Result<SkillDraft>.Ok)checkedDraft).Value;
            if (draft.Diff is { } diff)
            {
                await terminal.Error.WriteAsync($"{kind.Name}.md, version {draft.Version}:\n{diff}".AsMemory(), cancellationToken);
            }

            Directory.CreateDirectory(settings.Skills);
            await File.WriteAllTextAsync(path, draft.Text, cancellationToken);
            drafts.Add(draft);
        }

        await terminal.Output.WriteLineAsync(
            $"Drafted {string.Join(", ", drafts.Select(d => $"{d.Name} (version {d.Version})"))} in {settings.Skills}. Review, edit and commit them, then run 'hound skill validate --skills {settings.Skills}'. Nothing uses them until they are committed to your config repo's skills folder.".AsMemory(),
            cancellationToken);
        return ExitCodes.Ok;
    }

    /// <summary>What the config repo already says about the organisation: its systems, suppliers and capability map.</summary>
    private static async Task<Dictionary<string, string>> MaterialAsync(string repo, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] named = [Path.Combine("scope", "setup.yaml"), Path.Combine("scope", "capabilities.yaml")];
        var thirdParties = Path.Combine(repo, "scope", "third-parties");
        var paths = named.Select(n => Path.Combine(repo, n))
            .Concat(Directory.Exists(thirdParties) ? Directory.GetFiles(thirdParties, "*.yaml").Order(StringComparer.Ordinal) : Enumerable.Empty<string>())
            .Where(File.Exists);
        foreach (var path in paths)
        {
            files[Path.GetRelativePath(repo, path).Replace('\\', '/')] = await File.ReadAllTextAsync(path, cancellationToken);
        }

        return files;
    }
}
