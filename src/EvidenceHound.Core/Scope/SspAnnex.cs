using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The controls annex (s11-04, s11-16): each control in the programme's selected groups once, in the framework's order, with the parent nodes
/// it is listed under (for the Essential Eight, the strategies an ISM control serves) and the status, statement, owner and evidence of its row.
/// Its columns follow the ASD Blueprint SSP annex template: description, maturity levels, control type, responsible entity and the ASD
/// implementation status. A control with no row in the audit is not assessed: Not implemented on the SP 800-18 scale. Pure.
/// </summary>
public static class SspAnnex
{
    /// <summary>The values <c>responsible_entity</c> takes in <c>controls.yaml</c>.</summary>
    public static readonly string[] ResponsibleEntities = ["consumer", "shared", "outsourced_service_provider", "cloud_service_provider", "not_applicable"];

    public static readonly string Consumer = "Consumer";
    public static readonly string Shared = "Shared";
    public static readonly string NotAssessed = "Not assessed";

    private static readonly Dictionary<string, string> ControlTypeNames = new(StringComparer.Ordinal)
    {
        ["pol"] = "Policy",
        ["proc"] = "Procedure",
        ["tech"] = "Technical",
        ["doc"] = "Documentation",
    };

    private static readonly Dictionary<string, string> AsdStatuses = new(StringComparer.Ordinal)
    {
        ["compliant"] = "Effective",
        ["partially_compliant"] = "Ineffective",
        ["non_compliant"] = "Not implemented",
        ["not_applicable"] = "Not applicable",
    };

    public static IReadOnlyList<SspAnnexRow> Of(SspDocument document, FrameworkSnapshot snapshot, IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(groups);
        var rows = document.Requirements.Where(r => r.Ref.Value is not null).GroupBy(r => r.Ref.Value!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var controls = document.Controls.Where(c => c.RefId.Value is not null).ToDictionary(c => c.RefId.Value!, StringComparer.Ordinal);
        var supplied = document.ExternalServices.Select(s => s.System.Value).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return
        [
            .. snapshot.Nodes
                .Where(n => n.Groups.Any(g => groups.Contains(g, StringComparer.Ordinal)))
                .GroupBy(n => n.RefId ?? n.Urn, StringComparer.Ordinal)
                .Select(g => Row(g.Key, [.. g], document, rows, controls, supplied)),
        ];
    }

    /// <summary>The display name of a <c>responsible_entity</c> value, such as "Outsourced service provider".</summary>
    public static string EntityName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var words = value.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static SspAnnexRow Row(
        string control, SnapshotNode[] nodes, SspDocument document, Dictionary<string, SspRequirement> rows, Dictionary<string, SspControl> controls, HashSet<string> supplied)
    {
        var row = nodes.Select(n => n.RefId is not null && rows.TryGetValue(n.RefId, out var byRef) ? byRef : rows.GetValueOrDefault(n.Urn)).FirstOrDefault(r => r is not null);
        string[] parents = [.. nodes.Select(n => ProgrammeOutline.Name(n.ParentUrn)).Distinct(StringComparer.Ordinal)];
        var described = nodes.Select(n => n.Description).FirstOrDefault(d => d.Length > 0) ?? nodes[0].Title;
        string[] levels = [.. nodes.SelectMany(n => n.Groups).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] types = [.. ControlTypeNames.Values.Intersect(nodes.SelectMany(n => n.ReferenceControls).Select(Type).OfType<string>(), StringComparer.Ordinal)];
        if (row is null)
        {
            var none = new SspValue(null, new SspSource(SspSource.CisoAssistant, $"no requirement assessment for {control} in audit {document.Audit.Value}"));
            return new SspAnnexRow(control, nodes[0].Title, parents, none with { Value = "Applicable" }, none with { Value = SspStatements.NotImplemented }, none, none, none, null)
            {
                Description = described,
                MaturityLevels = levels,
                ControlTypes = types,
                ResponsibleEntity = new SspValue(Consumer, none.Source),
                AsdStatus = none with { Value = NotAssessed },
            };
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
            SspStatements.Unmet.Contains(row.Status.Value) ? PoamItem.For(row.Ref.Value) : null)
        {
            Description = described,
            MaturityLevels = levels,
            ControlTypes = types,
            ResponsibleEntity = Entity(SspStatements.Linked(row, controls), supplied, row.Result.Source),
            AsdStatus = new SspValue(row.Result.Value is { } result ? AsdStatuses.GetValueOrDefault(result, NotAssessed) : NotAssessed, row.Result.Source),
        };
    }

    /// <summary>The control type a reference control's id names by its prefix (<c>pol.</c>, <c>proc.</c>, <c>tech.</c>, <c>doc.</c>); null for any other.</summary>
    private static string? Type(string referenceControl)
    {
        var id = referenceControl[(referenceControl.LastIndexOf(':') + 1)..];
        var dot = id.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? ControlTypeNames.GetValueOrDefault(id[..dot]) : null;
    }

    /// <summary>A linked control's declared entity wins; otherwise Shared when a linked control covers a supplier-provided system, else Consumer.</summary>
    private static SspValue Entity(SspControl[] linked, HashSet<string> supplied, SspSource source)
    {
        if (linked.Select(c => c.ResponsibleEntity).FirstOrDefault(e => e?.Value is not null) is { } declared)
        {
            return declared;
        }

        var shared = linked.FirstOrDefault(c => c.Systems.Any(supplied.Contains));
        return shared is null ? new SspValue(Consumer, source) : new SspValue(Shared, shared.RefId.Source);
    }
}
