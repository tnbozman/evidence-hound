using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Explain;

public sealed record ExplainNote(string Name, string Note);

public sealed record ExplainReply(string Summary, IReadOnlyList<ExplainNote> Notes);

public static class Explanation
{
    public const string Step = "explain";

    public static Result<ExplainReply> Read(ChangeFacts facts, JsonNode reply) => throw new NotImplementedException();

    public static Task<Result<ExplainReply>> DraftAsync(IDraftingModel model, IReadOnlyList<Skill> skills, ChangeFacts facts, GuardTerms terms, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public static string Render(ChangeFacts facts, ExplainReply? reply, IReadOnlyList<Skill> skills) => throw new NotImplementedException();
}

public static class ExplainSkills
{
    public static Result<IReadOnlyList<Skill>> Choose(SkillCatalogue catalogue, IReadOnlyList<ChangeTopic> topics, string? framework, IReadOnlyList<string> named) =>
        throw new NotImplementedException();
}
