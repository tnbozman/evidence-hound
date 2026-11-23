using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary><c>scope/evidence-plan.yaml</c>: how each control's evidence arrives, from whom and how often.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] EvidencePlanKeys = ["grc_team", "evidence"];
    private static readonly string[] EvidenceRowKeys = ["ref_id", "control", "requirements", "method", "collector", "owner", "grc_team", "every", "runbook"];
    private static readonly string[] EvidenceMethods = ["automated", "manual", "hybrid"];
    private static readonly string[] Intervals = ["3 months", "6 months", "12 months"];

    private static ScopeEvidencePlan ReadEvidencePlan(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, EvidencePlanKeys);
        var grcTeam = file.Ref(root, "grc_team");
        return new ScopeEvidencePlan(grcTeam, [.. file.Items(root, "evidence").Select(item => ReadEvidenceRow(item, grcTeam, file)).OfType<ScopeEvidenceRow>()], file.At(root));
    }

    private static ScopeEvidenceRow? ReadEvidenceRow(YamlMappingNode item, ScopeRef? grcTeam, FileReader file)
    {
        file.Keys(item, EvidenceRowKeys);
        var refId = file.Required(item, "ref_id");
        var control = file.Ref(item, "control", required: true);
        var method = file.OneOf(item, "method", EvidenceMethods, required: true) switch
        {
            "automated" => EvidenceMethod.Automated,
            "manual" => EvidenceMethod.Manual,
            "hybrid" => EvidenceMethod.Hybrid,
            _ => (EvidenceMethod?)null,
        };
        var every = file.OneOf(item, "every", Intervals, required: false);
        if (refId is null || control is null || method is null)
        {
            return null;
        }

        var row = new ScopeEvidenceRow(refId, control, method.Value, file.At(item))
        {
            Requirements = Requirements(file.Refs(item, "requirements")),
            Collector = file.Ref(item, "collector"),
            Owner = file.Ref(item, "owner"),
            GrcTeam = file.Ref(item, "grc_team") ?? grcTeam,
            EveryMonths = every is null ? null : int.Parse(every.AsSpan(0, every.IndexOf(' ', StringComparison.Ordinal)), System.Globalization.CultureInfo.InvariantCulture),
            Runbook = file.Text(item, "runbook"),
        };
        RowNeeds(row, item, file);
        return row;
    }

    /// <summary>What a row's method needs: a person's row an owner, an interval and a runbook; a collector's row the collector.</summary>
    private static void RowNeeds(ScopeEvidenceRow row, YamlMappingNode item, FileReader file)
    {
        void Need(bool missing, string key, string code, string hint)
        {
            if (missing && FileReader.Child(item, key) is null)
            {
                file.Problem(code, row.At, $"evidence row '{row.RefId}' is {row.Method.ToString().ToLowerInvariant()} and has no '{key}'.", hint);
            }
        }

        Need(row.NeedsPerson && row.Owner is null, "owner", "evidence_plan.owner_missing", "Name the team that provides the evidence: a team in scope/access.yaml, such as an owner contact with an email.");
        Need(row.NeedsPerson && row.EveryMonths is null, "every", "evidence_plan.every_missing", "Say how often it is due: '3 months', '6 months' or '12 months'.");
        Need(row.NeedsPerson && row.Runbook is null, "runbook", "evidence_plan.runbook_missing", "Point 'runbook' at a Markdown file that says what to export, from where, and how to send it to the GRC team.");
        Need(row.NeedsCollector && row.Collector is null, "collector", "evidence_plan.collector_missing", "Name the collector instance from hound.yaml that gathers this evidence.");
    }
}
