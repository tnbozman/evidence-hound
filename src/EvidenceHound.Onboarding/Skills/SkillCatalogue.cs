using System.Reflection;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Skills;

/// <summary>
/// The active skills: the defaults built into hound plus the organisation's own. An organisation skill adds to the defaults; it replaces one
/// only when it says so with <c>overrides:</c>, so nothing is replaced by accident.
/// </summary>
public sealed record SkillCatalogue(IReadOnlyList<Skill> Skills, IReadOnlyDictionary<string, string> Overridden)
{
    internal const string ResourcePrefix = "skills/";

    /// <summary>The default skills, embedded from this repository's <c>skills/</c> folder.</summary>
    public static Result<IReadOnlyList<Skill>> Defaults()
    {
        var assembly = typeof(SkillCatalogue).Assembly;
        return Result.Combine(assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(name => SkillParser.Parse(Read(assembly, name), $"default:{name[ResourcePrefix.Length..]}", SkillSource.Default)));
    }

    /// <summary>The organisation's skills: every <c>*.md</c> in <paramref name="folder"/>. A missing folder has none.</summary>
    public static Result<IReadOnlyList<Skill>> FromFolder(string folder) =>
        !Directory.Exists(folder)
            ? Result.Ok<IReadOnlyList<Skill>>([])
            : Result.Combine(Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal)
                .Select(file => SkillParser.Parse(File.ReadAllText(file), file, SkillSource.Organisation)));

    public static Result<SkillCatalogue> Build(IReadOnlyList<Skill> defaults, IReadOnlyList<Skill> organisation)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(organisation);
        var defaultNames = defaults.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<Problem>();
        problems.AddRange(organisation.Where(s => defaultNames.Contains(s.Name) && s.Overrides != s.Name).Select(s => new Problem(
            "skill.name_taken",
            $"{s.Path}: '{s.Name}' is the name of a default skill. Rename it, or add 'overrides: {s.Name}' to replace the default on purpose.")));
        problems.AddRange(organisation.Where(s => s.Overrides is { } o && !defaultNames.Contains(o)).Select(s => new Problem(
            "skill.override_unknown", $"{s.Path}: overrides '{s.Overrides}', which is not a default skill. Run 'hound skill list' to see them.")));
        problems.AddRange(organisation.GroupBy(s => s.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => new Problem(
            "skill.duplicate", $"'{g.Key}' is the name of more than one organisation skill: {string.Join(", ", g.Select(s => s.Path))}.")));
        if (problems.Count > 0)
        {
            return Result.Fail<SkillCatalogue>(problems);
        }

        var overridden = organisation.Where(s => s.Overrides is not null).ToDictionary(s => s.Name, s => s.Overrides!, StringComparer.Ordinal);
        var replaced = overridden.Values.ToHashSet(StringComparer.Ordinal);
        return Result.Ok(new SkillCatalogue([.. defaults.Where(d => !replaced.Contains(d.Name)), .. organisation], overridden));
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Which skills guide a step. Pure.</summary>
public static class SkillSelection
{
    public const int DefaultBudgetCharacters = 60_000;

    /// <summary>
    /// Every skill for <paramref name="step"/> whose frameworks include <paramref name="framework"/> (or that names none), defaults first. Over the
    /// budget is a problem naming the skills, never a silent cut.
    /// </summary>
    public static Result<IReadOnlyList<Skill>> Select(SkillCatalogue catalogue, SkillStep step, string? framework, int budgetCharacters)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Skill[] chosen = [.. catalogue.Skills
            .Where(s => s.AppliesTo.Contains(step) && (s.Frameworks.Count == 0 || (framework is not null && s.Frameworks.Contains(framework, StringComparer.Ordinal))))
            .OrderBy(s => s.Source)];
        var size = chosen.Sum(s => s.Body.Length);
        return size <= budgetCharacters
            ? Result.Ok<IReadOnlyList<Skill>>(chosen)
            : Result.Fail<IReadOnlyList<Skill>>(new Problem(
                "skill.budget_exceeded",
                $"The skills for the {step.ToString().ToLowerInvariant()} step hold {size} characters, over the budget of {budgetCharacters}: {string.Join(", ", chosen.Select(s => $"{s.Name} ({s.Body.Length})"))}. Shorten one, or narrow its applies_to or frameworks."));
    }
}
