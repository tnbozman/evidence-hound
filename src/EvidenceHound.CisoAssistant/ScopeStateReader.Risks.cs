using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Risk studies, scenarios, security exceptions and risk acceptances, as the scope declares them (s15).</summary>
internal sealed partial class ScopeStateReader
{
    /// <summary>
    /// The risk studies under the root folder and each one's scenarios, with the matrix and threat URNs by ID (s15-02). Scenarios come from the
    /// list view, which gives each rating with its level name.
    /// </summary>
    private static async Task<RiskLists> RisksAsync(Reading reading, HashSet<string> inScope)
    {
        var studies = await reading.ObjectsInAsync("risk-assessments", inScope);
        var exceptions = await reading.ObjectsInAsync("security-exceptions", inScope);
        var acceptances = await reading.ObjectsInAsync("risk-acceptances", inScope);
        if (studies.Count == 0)
        {
            return RiskLists.None with { Exceptions = exceptions, Acceptances = acceptances };
        }

        var scenarios = new List<(string Study, JsonNode Scenario)>();
        foreach (var study in studies)
        {
            var key = RefIdOrName(ScopeKind.RiskStudy, study);
            scenarios.AddRange((await reading.ListAsync($"risk-scenarios/?risk_assessment={Uri.EscapeDataString(Id(study))}")).Select(s => (key, s)));
        }

        return new RiskLists(
            studies,
            scenarios,
            Names(await reading.ListAsync("risk-matrices/"), "urn"),
            scenarios.Count > 0 ? Names(await reading.ListAsync("threats/"), "urn") : [])
        {
            Exceptions = exceptions,
            Acceptances = acceptances,
        };
    }

    /// <summary>
    /// Security exceptions and risk acceptances (s15-03). An exception's controls and rows are read from their side (each one's
    /// <c>security_exceptions</c>); an acceptance is keyed by the scenario it accepts. Status and expiry are observed, never compared.
    /// </summary>
    private static IEnumerable<ActualObject> ExceptionObjects(RiskLists risks, Lookups names, List<JsonNode> controls, JsonNode[] rows, Dictionary<string, string> rowKeys)
    {
        string? LinkedFrom(IEnumerable<JsonNode> linking, string exception, Func<JsonNode, string?> key) =>
            ScopeDesired.Joined(linking.Where(o => (o["security_exceptions"] as JsonArray ?? []).Select(RelationId).Contains(exception)).Select(key).OfType<string>().Distinct(StringComparer.Ordinal));

        var scenarios = risks.Scenarios.ToDictionary(x => Id(x.Scenario), x => Core.Scope.RiskObjects.ScenarioKey(x.Study, Text(x.Scenario["ref_id"]) ?? Id(x.Scenario)), StringComparer.Ordinal);
        foreach (var e in risks.Exceptions)
        {
            yield return Marked(ScopeKind.SecurityException, RefIdOrName(ScopeKind.SecurityException, e), e,
                ("name", Text(e["name"])), ("domain", names.Domain(e)), ("severity", Severity(e["severity"])), ("owners", Joined(e["owners"], names.Users)),
                ("expiration_date", Text(e["expiration_date"])), ("observation", Text(e["observation"])), ("systems", Joined(e["assets"], names.Assets)),
                ("controls", LinkedFrom(controls, Id(e), c => names.Controls.GetValueOrDefault(Id(c)))),
                ("requirements", LinkedFrom(rows, Id(e), r => rowKeys.GetValueOrDefault(Id(r))))) with
            {
                Observed = Fields(("status", Text(e["status"])), ("expiration_date", Text(e["expiration_date"]))),
            };
        }

        foreach (var a in risks.Acceptances)
        {
            var scenario = (a["risk_scenarios"] as JsonArray ?? []).Select(RelationId).OfType<string>().Select(id => scenarios.GetValueOrDefault(id)).OfType<string>().FirstOrDefault();
            yield return Marked(ScopeKind.RiskAcceptance, scenario ?? Text(a["ref_id"]), a,
                ("name", Text(a["name"])), ("domain", names.Domain(a)), ("scenario", scenario), ("justification", Text(a["justification"]))) with
            {
                Observed = Fields(("status", Text(a["state"])), ("expiration_date", Text(a["expiry_date"]))),
            };
        }
    }

    /// <summary>A severity's name from CISO Assistant's number (-1 undefined to 4 critical).</summary>
    private static string? Severity(JsonNode? severity) =>
        severity is JsonValue v && v.TryGetValue<int>(out var number) && number >= -1 && number + 1 < ScopeExceptionRequest.Severities.Count
            ? ScopeExceptionRequest.Severities[number + 1]
            : null;

    private static IEnumerable<ActualObject> RiskObjects(RiskLists risks, Lookups names) =>
    [
        .. risks.Studies.Select(r => Marked(ScopeKind.RiskStudy, RefIdOrName(ScopeKind.RiskStudy, r), r,
            ("name", Text(r["name"])), ("domain", names.Domain(r)), ("perimeter", Lookup(r["perimeter"], names.Perimeters)), ("matrix", Lookup(r["risk_matrix"], risks.Matrices)),
            ("status", Text(r["status"])), ("eta", Text(r["eta"])), ("due_date", Text(r["due_date"])),
            ("authors", Joined(r["authors"], names.Users)), ("reviewers", Joined(r["reviewers"], names.Users)))),
        .. risks.Scenarios.Select(x => Marked(ScopeKind.RiskScenario, Core.Scope.RiskObjects.ScenarioKey(x.Study, Text(x.Scenario["ref_id"]) ?? Id(x.Scenario)), x.Scenario,
            ("ref_id", Text(x.Scenario["ref_id"])), ("name", Text(x.Scenario["name"])), ("study", x.Study),
            ("systems", Joined(x.Scenario["assets"], names.Assets)), ("threats", Joined(x.Scenario["threats"], risks.Threats)),
            ("existing_controls", Joined(x.Scenario["existing_applied_controls"], names.Controls)), ("planned_controls", Joined(x.Scenario["applied_controls"], names.Controls)),
            ("current_probability", Level(x.Scenario["current_proba"])), ("current_impact", Level(x.Scenario["current_impact"])),
            ("residual_probability", Level(x.Scenario["residual_proba"])), ("residual_impact", Level(x.Scenario["residual_impact"])),
            ("inherent_probability", Level(x.Scenario["inherent_proba"])), ("inherent_impact", Level(x.Scenario["inherent_impact"])),
            ("treatment", Text(x.Scenario["treatment"])), ("justification", Text(x.Scenario["justification"])),
            ("exceptions", Joined(x.Scenario["security_exceptions"], risks.Exceptions.ToDictionary(Id, e => RefIdOrName(ScopeKind.SecurityException, e) ?? Id(e), StringComparer.Ordinal))))),
    ];

    /// <summary>A rating's level name from the list view's <c>{"name", "value"}</c>; null when not rated (<c>--</c>).</summary>
    private static string? Level(JsonNode? rating) =>
        Text(rating?["name"]) is { } name && name != RiskRating.NotRatedName ? name : null;

    private sealed record RiskLists(
        List<JsonNode> Studies, List<(string Study, JsonNode Scenario)> Scenarios, Dictionary<string, string> Matrices, Dictionary<string, string> Threats)
    {
        public static RiskLists None { get; } = new([], [], [], []);

        public List<JsonNode> Exceptions { get; init; } = [];

        public List<JsonNode> Acceptances { get; init; } = [];
    }
}
