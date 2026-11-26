using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Business impact analyses, their assets and recovery objectives (s15-04).</summary>
internal sealed partial class ScopeStateReader
{
    /// <summary>The BIAs under the root folder (s15-04), each one's asset assessments, and each assessment's thresholds.</summary>
    private static async Task<BiaLists> BiasAsync(Reading reading, HashSet<string> inScope)
    {
        var bias = await reading.ObjectsInAsync("resilience/business-impact-analysis", inScope);
        if (bias.Count == 0)
        {
            return BiaLists.None;
        }

        var assessments = new List<(string Bia, JsonNode Assessment)>();
        var thresholds = new List<(string Assessment, JsonNode Threshold)>();
        foreach (var bia in bias)
        {
            var key = RefIdOrName(ScopeKind.Bia, bia);
            foreach (var assessment in await reading.ListAsync($"resilience/asset-assessments/?bia={Uri.EscapeDataString(Id(bia))}"))
            {
                var assessmentKey = Core.Scope.BiaObjects.AssessmentKey(key, Text(assessment["asset"]?["str"]) ?? Id(assessment));
                assessments.Add((key, assessment));
                thresholds.AddRange((await reading.ListAsync($"resilience/escalation-thresholds/?asset_assessment={Uri.EscapeDataString(Id(assessment))}")).Select(t => (assessmentKey, t)));
            }
        }

        return new BiaLists(bias, assessments, thresholds, Names(await reading.ListAsync("risk-matrices/"), "urn"));
    }

    private static IEnumerable<ActualObject> BiaObjects(BiaLists bias, Lookups names) =>
    [
        .. bias.Bias.Select(b => Marked(ScopeKind.Bia, RefIdOrName(ScopeKind.Bia, b), b,
            ("name", Text(b["name"])), ("domain", names.Domain(b)), ("perimeter", Lookup(b["perimeter"], names.Perimeters)), ("matrix", Lookup(b["risk_matrix"], bias.Matrices)),
            ("version", Text(b["version"])), ("status", Text(b["status"])), ("due_date", Text(b["due_date"])),
            ("authors", Joined(b["authors"], names.Users)), ("reviewers", Joined(b["reviewers"], names.Users)))),
        .. bias.Assessments.Select(x => Marked(ScopeKind.AssetAssessment, Core.Scope.BiaObjects.AssessmentKey(x.Bia, Text(x.Assessment["asset"]?["str"]) ?? Id(x.Assessment)), x.Assessment,
            ("bia", x.Bia), ("system", Text(x.Assessment["asset"]?["str"])), ("dependencies", Joined(x.Assessment["dependencies"], names.Assets)),
            ("controls", Joined(x.Assessment["associated_controls"], names.Controls)), ("recovery_documented", Flag(x.Assessment["recovery_documented"])),
            ("recovery_tested", Flag(x.Assessment["recovery_tested"])), ("recovery_targets_met", Flag(x.Assessment["recovery_targets_met"])),
            ("observation", Text(x.Assessment["observation"])))),
        .. bias.Thresholds.Select(x =>
        {
            var seconds = x.Threshold["point_in_time"] is JsonValue v && v.TryGetValue<int>(out var pit) ? pit : 0;
            return Marked(ScopeKind.EscalationThreshold, Core.Scope.BiaObjects.ThresholdKey(x.Assessment, new Duration(seconds)), x.Threshold,
                ("assessment", x.Assessment), ("point_in_time", seconds.ToString(CultureInfo.InvariantCulture)), ("impact", Level(x.Threshold["quali_impact"])),
                ("qualifications", ScopeDesired.Joined((x.Threshold["qualifications"] as JsonArray ?? []).Select(q => Text(q?["str"])).OfType<string>())),
                ("justification", Text(x.Threshold["justification"])));
        }),
    ];

    /// <summary>A system's enabled security objectives, as <c>name=value</c> in name order.</summary>
    private static string? Objectives(JsonNode? held) =>
        ScopeDesired.Joined((held?["objectives"] as JsonObject ?? []).Where(o => o.Value?["is_enabled"] is JsonValue e && e.TryGetValue<bool>(out var on) && on)
            .Where(o => o.Value?["value"] is JsonValue v && v.TryGetValue<int>(out _)).OrderBy(o => o.Key, StringComparer.Ordinal)
            .Select(o => string.Create(CultureInfo.InvariantCulture, $"{o.Key}={o.Value!["value"]!.GetValue<int>()}")));

    /// <summary>A system's recovery objectives, as <c>name=duration</c> in name order.</summary>
    private static string? RecoveryObjectives(JsonNode? held) =>
        ScopeDesired.Joined((held?["objectives"] as JsonObject ?? []).Where(o => o.Value?["value"] is JsonValue v && v.TryGetValue<int>(out var s) && s > 0)
            .OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={new Duration(o.Value!["value"]!.GetValue<int>())}"));

    private sealed record BiaLists(
        List<JsonNode> Bias, List<(string Bia, JsonNode Assessment)> Assessments, List<(string Assessment, JsonNode Threshold)> Thresholds, Dictionary<string, string> Matrices)
    {
        public static BiaLists None { get; } = new([], [], [], []);
    }
}
