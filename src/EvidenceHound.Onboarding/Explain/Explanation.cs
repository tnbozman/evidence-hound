using System.Globalization;
using System.Text;

namespace EvidenceHound.Onboarding.Explain;

/// <summary>
/// Explaining a change for review: the facts read in code from a plan or a diff, as review text. Review only: nothing here edits config or
/// applies a plan. Pure.
/// </summary>
public static class Explanation
{
    /// <summary>The review text: the counts as the source of truth and what needs a careful look.</summary>
    public static string Render(ChangeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"## Change explained: {facts.Source}\n\n")
            .Append(CultureInfo.InvariantCulture, $"The {facts.Kind} is the source of truth: {facts.CountsText} This summary helps you review it; it does not replace reading it.\n\n")
            .Append("### Needs a careful look\n\n");
        text.Append(facts.Flags.Count == 0 ? "Nothing flagged.\n" : string.Concat(facts.Flags.Select(f => $"- {f}\n")));
        return text.ToString();
    }
}
