using System.Globalization;
using System.Text;

namespace EvidenceHound.Core.Scope;

/// <summary>One line of the plan of action and milestones: a requirement not yet met and a control that answers it.</summary>
public sealed record PoamItem(SspValue Requirement, SspValue Status, SspValue Control, SspValue Owner, SspValue Eta, SspValue ControlStatus)
{
    /// <summary>The item's id, which the annex links to: one per requirement.</summary>
    public string Id => For(Requirement.Value);

    /// <summary>The POA&amp;M id of a requirement.</summary>
    public static string For(string? requirement) => $"POAM-{requirement}";

    /// <summary>Why CMMC does not allow this item on a POA&amp;M (s17-03); null when it may sit there.</summary>
    public string? NotAllowed { get; init; }
}

/// <summary>
/// The POA&amp;M, from the same model as the plan (s11-03): every Partially implemented, Planned or Not implemented requirement, one line per
/// linked control with its owner, ETA and status from CISO Assistant, or one line with no control. Ordered by requirement, then control. Pure.
/// </summary>
public static class Poam
{
    public static IReadOnlyList<PoamItem> Of(SspDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var controls = document.Controls.Where(c => c.RefId.Value is not null).ToDictionary(c => c.RefId.Value!, StringComparer.Ordinal);
        return
        [
            .. document.Requirements
                .Where(r => SspStatements.Unmet.Contains(r.Status.Value))
                .OrderBy(r => r.Ref.Value, StringComparer.Ordinal)
                .SelectMany(r => Items(r, SspStatements.Linked(r, controls)))
                .Select(i => i with { NotAllowed = document.PoamNotAllowed.GetValueOrDefault(i.Requirement.Value ?? "") }),
        ];
    }

    public static string Markdown(IReadOnlyList<PoamItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var text = new StringBuilder()
            .Append("## Plan of action and milestones\n\n")
            .Append("| Requirement | Status | Control | Owner | ETA | Control status |\n")
            .Append("| --- | --- | --- | --- | --- | --- |\n");
        foreach (var i in items)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {i.Requirement.Value} | {i.Status.Value} | {Cell(i.Control)} | {Cell(i.Owner)} | {Cell(i.Eta)} | {Cell(i.ControlStatus)} |\n");
        }

        text.Append(items.Count == 0 ? "\nNothing to plan: every requirement is implemented or not applicable.\n" : "");
        var refused = items.Where(i => i.NotAllowed is not null).DistinctBy(i => i.Requirement.Value).ToArray();
        if (refused.Length > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nCMMC does not allow these on a POA&M ({CmmcPoam.Source}):\n\n");
            foreach (var i in refused)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {i.Requirement.Value}: {i.NotAllowed}\n");
            }
        }

        return text.ToString();
    }

    private static IEnumerable<PoamItem> Items(SspRequirement row, SspControl[] linked) =>
        linked.Length == 0
            ? [new PoamItem(row.Ref, row.Status, None(row), None(row), None(row), None(row))]
            : linked.Select(c => new PoamItem(row.Ref, row.Status, c.RefId, c.Owner, c.Eta, c.Status));

    private static SspValue None(SspRequirement row) => new(null, row.Result.Source);

    private static string Cell(SspValue value) => value.Value ?? "-";
}
