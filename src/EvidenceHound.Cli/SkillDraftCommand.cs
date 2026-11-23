using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Skills;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound skill draft</c>: the organisation's own skills as templates, their questions as headings, written only into the organisation's
/// skills folder for a person, or their coding assistant, to fill in and review.
/// </summary>
internal static class SkillDraftCommand
{
    public sealed record Settings(string Skills, IReadOnlyList<string> Only);

    public static async Task<int> RunAsync(Settings settings, Terminal terminal, ILogger logger, CancellationToken cancellationToken)
    {
        var unknown = settings.Only.Where(n => OrganisationSkills.Kinds.All(k => k.Name != n)).ToArray();
        if (unknown.Length > 0)
        {
            Log.Usage(logger, $"Not a skill hound drafts: {string.Join(", ", unknown)}. Choose from {string.Join(", ", OrganisationSkills.Kinds.Select(k => k.Name))}.");
            return ExitCodes.Usage;
        }

        var kinds = OrganisationSkills.Kinds.Where(k => settings.Only.Count == 0 || settings.Only.Contains(k.Name)).ToArray();
        var written = new List<SkillDraft>();
        var kept = new List<string>();
        foreach (var kind in kinds)
        {
            // A skill that is already there is the organisation's own: its answers are never replaced by a template.
            var path = Path.Combine(settings.Skills, $"{kind.Name}.md");
            if (File.Exists(path))
            {
                kept.Add(kind.Name);
                continue;
            }

            var template = OrganisationSkills.Template(kind, null);
            if (SkillParser.Parse(template.Text, path, SkillSource.Organisation) is Result<Skill>.Fail { Errors: var problems })
            {
                return Log.Problems(logger, problems);
            }

            Directory.CreateDirectory(settings.Skills);
            await File.WriteAllTextAsync(path, template.Text, cancellationToken);
            written.Add(template);
        }

        var wrote = written.Count > 0 ? $"Wrote {string.Join(", ", written.Select(d => $"{d.Name} (version {d.Version})"))} in {settings.Skills}. " : "";
        var left = kept.Count > 0 ? $"Kept {string.Join(", ", kept)}: already there, and yours. " : "";
        await terminal.Output.WriteLineAsync(
            $"{wrote}{left}Answer each template's questions (or ask your coding assistant to, from what the repo holds), review and commit them, then run 'hound skill validate --skills {settings.Skills}'. Nothing uses them until they are committed to your config repo's skills folder.".AsMemory(),
            cancellationToken);
        return ExitCodes.Ok;
    }
}
