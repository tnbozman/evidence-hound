using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Explain;

/// <summary>A point the model makes about one object the plan or diff names.</summary>
public sealed record ExplainNote(string Name, string Note);

/// <summary>The model's review summary: a draft to help the reviewer, never the source of truth.</summary>
public sealed record ExplainReply(string Summary, IReadOnlyList<ExplainNote> Notes);

/// <summary>
/// Explaining a change for review: the model's summary of facts read in code, checked against them, and the review text. Review only: nothing
/// here edits config or applies a plan. Pure except <see cref="DraftAsync"/>.
/// </summary>
public static class Explanation
{
    public const string Step = "explain";

    internal const string Instructions = """
        You help a GRC team review a change to their compliance configuration before it is applied. Summarise in plain English what the change
        does, for a reviewer who must approve only what they understand: which systems, audits, requirements, suppliers, profiles or mappings it
        affects and why it matters. Point out anything risky or inconsistent: requirements newly marked not applicable, removed checks or controls,
        deletions, a system without an owner, a supplier not yet approved, a mapping that no longer matches a profile's checks. The counts and the
        flags given were read by code and are correct; do not restate different numbers. In notes, name only objects listed in the objects input,
        exactly as listed.
        """;

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["notes"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["object"] = new JsonObject { ["type"] = "string" }, ["note"] = new JsonObject { ["type"] = "string" } },
                    ["required"] = new JsonArray("object", "note"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("summary", "notes"),
        ["additionalProperties"] = false,
    };

    /// <summary>The model's reply, checked: a summary, and notes that name only objects the plan or diff contains.</summary>
    public static Result<ExplainReply> Read(ChangeFacts facts, JsonNode reply) => Read(facts, reply, GuardTerms.None);

    public static async Task<Result<ExplainReply>> DraftAsync(IDraftingModel model, IReadOnlyList<Skill> skills, ChangeFacts facts, GuardTerms terms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(terms);
        var redact = (string text) => OutboundGuard.Redact(text, terms);
        var request = StepRequest.Compose(
            Instructions,
            skills,
            [
                new DraftInput("counts", $"Counts read by code from the {facts.Kind}, the source of truth: {facts.CountsText}"),
                new DraftInput("flags", facts.Flags.Count == 0 ? "None." : redact(string.Join('\n', facts.Flags.Select(f => $"- {f}")))),
                new DraftInput("objects", redact(string.Join('\n', facts.Objects))),
                new DraftInput("change", redact(facts.Text)),
            ],
            Schema(),
            Step);
        return (await model.DraftAsync(request, cancellationToken)).Bind(r => Read(facts, r.Value, terms));
    }

    /// <summary>The review text: the counts as the source of truth, what needs a careful look, the model's summary when there is one, and the skills used.</summary>
    public static string Render(ChangeFacts facts, ExplainReply? reply, IReadOnlyList<Skill> skills)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(skills);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"## Change explained: {facts.Source}\n\n")
            .Append(CultureInfo.InvariantCulture, $"The {facts.Kind} is the source of truth: {facts.CountsText} This summary helps you review it; it does not replace reading it.\n\n")
            .Append("### Needs a careful look\n\n");
        text.Append(facts.Flags.Count == 0 ? "Nothing flagged.\n" : string.Concat(facts.Flags.Select(f => $"- {f}\n")));
        text.Append("\n### Summary\n\n");
        if (reply is null)
        {
            text.Append("No model summary (--no-model).\n");
        }
        else
        {
            text.Append(reply.Summary.Trim()).Append("\n\n");
            text.Append(string.Concat(reply.Notes.Select(n => $"- {n.Name}: {n.Note.Trim()}\n")));
            text.Append("\nThis summary is a draft by a model; check it against the counts and the change.\n");
        }

        if (skills.Count > 0)
        {
            text.Append("\n### Skills\n\n| Skill | Source | Version | SHA-256 |\n| --- | --- | --- | --- |\n");
            foreach (var skill in skills)
            {
                text.Append(CultureInfo.InvariantCulture, $"| {skill.Name} | {skill.Source.ToString().ToLowerInvariant()} | {skill.Version} | `{skill.Sha256}` |\n");
            }
        }

        return text.ToString();
    }

    private static Result<ExplainReply> Read(ChangeFacts facts, JsonNode reply, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (reply is not JsonObject root || root["summary"] is not JsonValue summaryNode || !summaryNode.TryGetValue<string>(out var summary) || summary.Trim().Length == 0)
        {
            return Result.Fail<ExplainReply>(LlmProblems.InvalidReply("The reply has no summary."));
        }

        var known = facts.Objects.Concat(facts.Objects.Select(o => OutboundGuard.Redact(o, terms))).ToHashSet(StringComparer.Ordinal);
        var notes = new List<ExplainNote>();
        foreach (var item in root["notes"] as JsonArray ?? [])
        {
            var name = (string?)item?["object"];
            var note = (string?)item?["note"];
            if (name is null || note is null)
            {
                return Result.Fail<ExplainReply>(LlmProblems.InvalidReply("Each note needs an object and a note."));
            }

            if (!known.Contains(name))
            {
                return Result.Fail<ExplainReply>(LlmProblems.InvalidReply($"The reply names '{name}', which the {facts.Kind} does not contain."));
            }

            notes.Add(new ExplainNote(name, note));
        }

        return Result.Ok(new ExplainReply(summary, notes));
    }
}

/// <summary>The skills for explaining a change: the explain step's own, plus those of the onboarding steps the change touches. Pure.</summary>
public static class ExplainSkills
{
    private static readonly Dictionary<ChangeTopic, SkillStep> Steps = new()
    {
        [ChangeTopic.ThirdParty] = SkillStep.Supplier,
        [ChangeTopic.NotApplicable] = SkillStep.Applicability,
        [ChangeTopic.Profile] = SkillStep.Profile,
        [ChangeTopic.Mapping] = SkillStep.Mapping,
        [ChangeTopic.Audit] = SkillStep.Mapping,
    };

    /// <summary>With <paramref name="named"/>, exactly those skills; otherwise the explain skills and those for each topic, for <paramref name="framework"/>.</summary>
    public static Result<IReadOnlyList<Skill>> Choose(SkillCatalogue catalogue, IReadOnlyList<ChangeTopic> topics, string? framework, IReadOnlyList<string> named)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(named);
        if (named.Count > 0)
        {
            var missing = named.Where(n => !catalogue.Skills.Any(s => s.Name == n)).ToArray();
            return missing.Length > 0
                ? Result.Fail<IReadOnlyList<Skill>>([.. missing.Select(n => new Problem("skill.unknown", $"'{n}' is not a skill. Run 'hound skill list' to see them."))])
                : Result.Ok<IReadOnlyList<Skill>>([.. named.Distinct(StringComparer.Ordinal).Select(n => catalogue.Skills.First(s => s.Name == n))]);
        }

        var steps = topics.Select(t => Steps[t]).Append(SkillStep.Explain).ToHashSet();
        Skill[] chosen = [.. catalogue.Skills
            .Where(s => s.AppliesTo.Any(steps.Contains) && (s.Frameworks.Count == 0 || (framework is not null && s.Frameworks.Contains(framework, StringComparer.Ordinal))))
            .OrderBy(s => s.Source)];
        var size = chosen.Sum(s => s.Body.Length);
        return size <= SkillSelection.DefaultBudgetCharacters
            ? Result.Ok<IReadOnlyList<Skill>>(chosen)
            : Result.Fail<IReadOnlyList<Skill>>(new Problem(
                "skill.budget_exceeded",
                $"The skills for this change hold {size} characters, over the budget of {SkillSelection.DefaultBudgetCharacters}: {string.Join(", ", chosen.Select(s => s.Name))}. Choose fewer with --skill."));
    }
}
