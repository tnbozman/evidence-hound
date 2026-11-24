using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The controls annex (s11-04): each control in the programme's selected groups once, in the framework's order, with the parent nodes it is
/// listed under (for the Essential Eight, the strategies an ISM control serves) and the status, statement, owner and evidence of its row.
/// A control with no row in the audit is not assessed: Not implemented. Pure.
/// </summary>
public static class SspAnnex
{
    public static IReadOnlyList<SspAnnexRow> Of(SspDocument document, FrameworkSnapshot snapshot, IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(groups);
        var rows = document.Requirements.Where(r => r.Ref.Value is not null).GroupBy(r => r.Ref.Value!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return
        [
            .. snapshot.Nodes
                .Where(n => n.Groups.Any(g => groups.Contains(g, StringComparer.Ordinal)))
                .GroupBy(n => n.RefId ?? n.Urn, StringComparer.Ordinal)
                .Select(g => Row(g.Key, [.. g], document, rows)),
        ];
    }

    private static SspAnnexRow Row(string control, SnapshotNode[] nodes, SspDocument document, Dictionary<string, SspRequirement> rows)
    {
        var row = nodes.Select(n => n.RefId is not null && rows.TryGetValue(n.RefId, out var byRef) ? byRef : rows.GetValueOrDefault(n.Urn)).FirstOrDefault(r => r is not null);
        string[] parents = [.. nodes.Select(n => ProgrammeOutline.Name(n.ParentUrn)).Distinct(StringComparer.Ordinal)];
        if (row is null)
        {
            var none = new SspValue(null, new SspSource(SspSource.CisoAssistant, $"no requirement assessment for {control} in audit {document.Audit.Value}"));
            return new SspAnnexRow(control, nodes[0].Title, parents, none with { Value = "Applicable" }, none with { Value = SspStatements.NotImplemented }, none, none, none, null);
        }

        var notApplicable = row.Status.Value == SspStatements.NotApplicable;
        return new SspAnnexRow(
            control,
            nodes[0].Title,
            parents,
            new SspValue(notApplicable ? "Not applicable" : "Applicable", row.Status.Source),
            row.Status,
            row.Statement,
            row.Responsible,
            row.Evidence,
            SspStatements.Unmet.Contains(row.Status.Value) ? PoamItem.For(row.Ref.Value) : null);
    }
}
