using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Results;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// s12-04: reads applied controls, risk acceptances, security exceptions, findings, incidents, risk scenarios, task occurrences and
/// supplier reviews for a posture snapshot. Read only. Fields missing from a CISO Assistant version are read as empty, not errors.
/// </summary>
internal sealed class PostureReader(HttpClient http) : IPostureSource
{
    public async Task<Result<PostureRecords>> ReadAsync(CancellationToken cancellationToken)
    {
        var controls = await http.ListAllAsync("applied-controls/", cancellationToken);
        var acceptances = await http.ListAllAsync("risk-acceptances/", cancellationToken);
        var exceptions = await http.ListAllAsync("security-exceptions/", cancellationToken);
        var findings = await http.ListAllAsync("findings/", cancellationToken);
        var incidents = await http.ListAllAsync("incidents/", cancellationToken);
        var risks = await http.ListAllAsync("risk-scenarios/", cancellationToken);
        var tasks = await http.ListAllAsync("task-nodes/", cancellationToken);
        var reviews = await http.ListAllAsync("entity-assessments/", cancellationToken);
        return controls.Bind(c => acceptances.Bind(a => exceptions.Bind(e => findings.Bind(f => incidents.Bind(i => risks.Bind(r => tasks.Bind(t => reviews.Map(v =>
            new PostureRecords(
                [.. c.Select(Control)],
                [.. a.Select(Acceptance), .. e.Select(Exception)],
                [.. f.Select(Finding)],
                [.. i.Select(Incident)],
                [.. r.Select(Risk)],
                [.. t.Select(Task), .. v.Where(x => Date(x["due_date"]) is not null).Select(Review)])))))))));
    }

    public string Link(string resource, string id) => new Uri(WebApp, $"{resource}/{Uri.EscapeDataString(id)}").ToString();

    /// <summary>The web app's address: the API's address without its trailing <c>api/</c>.</summary>
    private Uri WebApp => new(http.BaseAddress!, "../");

    private ControlItem Control(JsonNode node) =>
        new(Text(node["ref_id"]) ?? "", Text(node["name"]) ?? "", Text(node["status"]) ?? "", Names(node["owner"]), Date(node["eta"])) { Link = LinkOf("applied-controls", node) };

    private AcceptanceItem Acceptance(JsonNode node) => new(
        AcceptanceKind.RiskAcceptance, Text(node["ref_id"]) ?? "", Text(node["name"]) ?? "", Text(node["state"]) ?? "", Names(node["approver"]), Date(node["accepted_at"]), Date(node["expiry_date"]))
    {
        Link = LinkOf("risk-acceptances", node),
    };

    private AcceptanceItem Exception(JsonNode node) => new(
        AcceptanceKind.SecurityException,
        Text(node["ref_id"]) ?? "",
        Text(node["name"]) ?? "",
        Text(node["status"]) ?? "",
        Names(node["approver"]),
        Date(node["approved_at"]) ?? Date(node["created_at"]),
        Date(node["expiration_date"]))
    {
        Link = LinkOf("security-exceptions", node),
    };

    private RemediationItem Finding(JsonNode node) => new(
        Text(node["ref_id"]) ?? "", Text(node["name"]) ?? "", Names(node["owner"]), Date(node["eta"]) ?? Date(node["due_date"]), Text(node["status"]) ?? "", null)
    {
        Link = LinkOf("findings", node),
    };

    private IncidentItem Incident(JsonNode node)
    {
        var status = Text(node["status"]) ?? "";
        return new IncidentItem(
            Text(node["ref_id"]) ?? "",
            Text(node["name"]) ?? "",
            status,
            Text(node["severity"]),
            Date(node["reported_at"]) ?? Date(node["created_at"]),
            status is "closed" or "resolved" or "dismissed" ? Date(node["updated_at"]) : null)
        {
            Link = LinkOf("incidents", node),
        };
    }

    private RiskItem Risk(JsonNode node) => new(
        Text(node["ref_id"]) ?? "",
        Text(node["name"]) ?? "",
        Level(node["current_level"]),
        Level(node["residual_level"]),
        Rank(node["residual_level"]) ?? Rank(node["current_level"]) ?? -1,
        Names(node["owner"]),
        Text(node["treatment"]),
        Date(node["eta"]))
    {
        Link = LinkOf("risk-scenarios", node),
    };

    private DueItem Task(JsonNode node) => new(
        DueKind.ManualEvidence, Names(node["task_template"]) ?? Text(node["name"]) ?? "", Names(node["assigned_to"]), Date(node["due_date"]) ?? DateOnly.MinValue, Text(node["status"]) ?? "")
    {
        Link = LinkOf("task-nodes", node),
    };

    private DueItem Review(JsonNode node) => new(
        DueKind.SupplierReview,
        Text(node["name"]) ?? "",
        Names(node["authors"]),
        Date(node["due_date"])!.Value,
        Text(node["status"]) is "done" || Text(node["conclusion"]) is { Length: > 0 } ? "completed" : Text(node["status"]) ?? "")
    {
        Link = LinkOf("entity-assessments", node),
    };

    private string? LinkOf(string resource, JsonNode node) => Text(node["id"]) is { } id ? Link(resource, id) : null;

    private static string? Text(JsonNode? node) => node is JsonValue value ? value.ToString() : null;

    /// <summary>A person or object as CISO Assistant shows it: <c>{"str": ...}</c>, a list of those (joined), or plain text. Null when none.</summary>
    private static string? Names(JsonNode? node) => node switch
    {
        JsonArray list => list.Select(Names).OfType<string>().ToList() is { Count: > 0 } names ? string.Join(", ", names) : null,
        JsonObject item => Text(item["str"]) ?? Text(item["name"]) ?? Text(item["email"]),
        JsonValue value => value.ToString() is { Length: > 0 } text ? text : null,
        _ => null,
    };

    private static string? Level(JsonNode? node) => node is JsonObject level ? Text(level["name"]) : Text(node);

    private static int? Rank(JsonNode? node) =>
        node is JsonObject level && int.TryParse(Text(level["value"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ? rank : null;

    private static DateOnly? Date(JsonNode? node) =>
        Text(node) is { Length: >= 10 } text && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
