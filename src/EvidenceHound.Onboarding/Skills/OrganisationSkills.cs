using System.Globalization;
using System.Text;
using EvidenceHound.Onboarding.Explain;

namespace EvidenceHound.Onboarding.Skills;

/// <summary>An organisation skill onboarding can draft: its name, what it is for, the steps it guides, and the questions that fill it.</summary>
public sealed record OrganisationSkillKind(string Name, string Description, IReadOnlyList<string> AppliesTo, IReadOnlyList<string> Questions);

/// <summary>A drafted skill file: its text with frontmatter, its version, and the change from the file it replaces.</summary>
public sealed record SkillDraft(string Name, string Text, string Version, string? Diff);

/// <summary>
/// Organisation skills for the organisation's own skills folder, written as templates whose questions a person, or their coding assistant,
/// answers. A skill starts at version 1 and goes up each time it is written again. Pure.
/// </summary>
public static class OrganisationSkills
{
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

    /// <summary>The skill as a template, its questions as headings.</summary>
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
}
