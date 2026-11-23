using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Skills;

/// <summary>An organisation skill onboarding can draft: its name, what it is for, the steps it guides, and the questions that fill it.</summary>
public sealed record OrganisationSkillKind(string Name, string Description, IReadOnlyList<string> AppliesTo, IReadOnlyList<string> Questions);

public sealed record SkillAnswer(string Question, string Answer);

/// <summary>A drafted skill file: its text with frontmatter, its version, and the change from the file it replaces.</summary>
public sealed record SkillDraft(string Name, string Text, string Version, string? Diff);

public static class OrganisationSkills
{
    public const string Step = "skill_draft";

    public static IReadOnlyList<OrganisationSkillKind> Kinds { get; } = [];

    public static IReadOnlyList<DraftInput> Material(IReadOnlyDictionary<string, string> files, GuardTerms terms) => throw new NotImplementedException();

    public static Task<Result<string>> DraftAsync(IDraftingModel model, OrganisationSkillKind kind, IReadOnlyList<SkillAnswer> answers, IReadOnlyList<DraftInput> material, GuardTerms terms, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public static SkillDraft Draft(OrganisationSkillKind kind, string body, string? existing, GuardTerms terms) => throw new NotImplementedException();

    public static SkillDraft Template(OrganisationSkillKind kind, string? existing) => throw new NotImplementedException();
}
