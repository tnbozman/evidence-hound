using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Explain;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Skills;

/// <summary>An organisation skill onboarding can draft: its name, what it is for, the steps it guides, and the questions that fill it.</summary>
public sealed record OrganisationSkillKind(string Name, string Description, IReadOnlyList<string> AppliesTo, IReadOnlyList<string> Questions);

public sealed record SkillAnswer(string Question, string Answer);

/// <summary>A drafted skill file: its text with frontmatter, its version, and the change from the file it replaces.</summary>
public sealed record SkillDraft(string Name, string Text, string Version, string? Diff);

/// <summary>
/// Organisation skills drafted for the organisation's own skills folder: from the person's answers and what the config repo holds, through
/// the onboarding gate. Every draft is stripped of identifiers before it is written, starts at version 1 and goes up on each re-draft.
/// Pure except <see cref="DraftAsync"/>.
/// </summary>
public static class OrganisationSkills
{
    public const string Step = "skill_draft";

    internal const string Instructions = """
        You help a GRC team write guidance about their own organisation for an assistant that helps them onboard systems and suppliers and
        review configuration changes. Write the body of one Markdown skill, in plain English, as short rules and facts the assistant should
        follow: what the organisation does, what it will and will not accept, and how its systems fit together. Use only the answers and the
        configuration material given; where they do not say, leave the point out rather than guessing. Never include secrets, host names,
        IP addresses, tenant or account IDs, or people's names and contact details; refer to roles instead. Return the body only, without
        frontmatter.
        """;

    public static IReadOnlyList<OrganisationSkillKind> Kinds { get; } =
    [
        new(
            "organisation-profile",
            "What the organisation does, its obligations, and who decides.",
            ["supplier", "system", "applicability", "explain"],
            [
                "What does the organisation do, and for whom?",
                "How many staff are there, and where do they work?",
                "Which frameworks and maturity targets apply, and why?",
                "Which role approves risks and not-applicable decisions?",
            ]),
        new(
            "risk-appetite",
            "The risks the organisation accepts and those it does not.",
            ["supplier", "applicability", "explain"],
            [
                "Which risks will the organisation not accept?",
                "What assurance must a supplier show before it is approved?",
                "Which data must stay in the country, or never leave the organisation?",
                "How long may a known gap stay open before it is escalated?",
            ]),
        new(
            "it-landscape",
            "How the organisation's IT fits together: identity, devices, hosting and network.",
            ["system", "applicability", "explain"],
            [
                "Which identity provider and device management do staff use?",
                "Where are systems hosted, and how do offices connect to them?",
                "Which systems provide controls for others, such as backups or application control?",
                "How are systems and accounts named?",
            ]),
        new(
            "ai-landscape",
            "Which AI systems the organisation uses, and the rules around them.",
            ["supplier", "system", "explain"],
            [
                "Which AI systems or features are in use, and by whom?",
                "Which data may and may not be given to AI systems?",
                "Who approves a new AI system or use?",
            ]),
    ];

    /// <summary>The config repo's files (path to text) as inputs for the model, with identifiers replaced.</summary>
    public static IReadOnlyList<DraftInput> Material(IReadOnlyDictionary<string, string> files, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(terms);
        return [.. files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new DraftInput(f.Key, OutboundGuard.Redact(f.Value, terms)))];
    }

    public static async Task<Result<string>> DraftAsync(
        IDraftingModel model, OrganisationSkillKind kind, IReadOnlyList<SkillAnswer> answers, IReadOnlyList<DraftInput> material, GuardTerms terms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(terms);
        var request = StepRequest.Compose(
            Instructions,
            [],
            [
                new DraftInput("skill", $"Name: {kind.Name}\nPurpose: {kind.Description}\nGuides: {string.Join(", ", kind.AppliesTo)}"),
                new DraftInput("answers", OutboundGuard.Redact(string.Join("\n\n", answers.Where(a => a.Answer.Trim().Length > 0).Select(a => $"Q: {a.Question}\nA: {a.Answer.Trim()}")), terms)),
                .. material,
            ],
            Schema(),
            Step);
        return (await model.DraftAsync(request, cancellationToken)).Bind(reply =>
            reply.Value is JsonObject root && root["body"] is JsonValue value && value.TryGetValue<string>(out var body) && body.Trim().Length > 0
                ? Result.Ok(body.Trim())
                : Result.Fail<string>(LlmProblems.InvalidReply("The reply has no skill body.")));
    }

    /// <summary>The skill file for <paramref name="body"/>, stripped of identifiers, with the version after <paramref name="existing"/>'s.</summary>
    public static SkillDraft Draft(OrganisationSkillKind kind, string body, string? existing, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(body);
        return Build(kind, OutboundGuard.Redact(body.Trim(), terms), existing);
    }

    /// <summary>The skill as a template, its questions as headings, for teams without a model.</summary>
    public static SkillDraft Template(OrganisationSkillKind kind, string? existing)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var body = new StringBuilder().Append(CultureInfo.InvariantCulture, $"# {kind.Description}\n\nAnswer each question in a few short rules or facts. Refer to roles, not people; leave out host names, addresses and IDs.");
        foreach (var question in kind.Questions)
        {
            body.Append(CultureInfo.InvariantCulture, $"\n\n## {question}\n\n_Answer here._");
        }

        return Build(kind, body.ToString(), existing);
    }

    private static SkillDraft Build(OrganisationSkillKind kind, string body, string? existing)
    {
        var version = existing is null ? "1" : Next(existing);
        var text = $"""
            ---
            name: {kind.Name}
            description: '{kind.Description.Replace("'", "''", StringComparison.Ordinal)}'
            version: {version}
            applies_to: [{string.Join(", ", kind.AppliesTo)}]
            ---

            """ + "\n" + body + "\n";
        return new SkillDraft(kind.Name, text, version, existing is null ? null : ChangeReader.Diff(existing, text, $"{kind.Name}.md"));
    }

    /// <summary>The version after the one <paramref name="existing"/> declares: a whole number goes up by one, anything else restarts at 2.</summary>
    private static string Next(string existing)
    {
        var line = existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').FirstOrDefault(l => l.StartsWith("version:", StringComparison.Ordinal));
        return line is not null && int.TryParse(line["version:".Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? (number + 1).ToString(CultureInfo.InvariantCulture)
            : "2";
    }

    private static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["body"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("body"),
        ["additionalProperties"] = false,
    };
}
