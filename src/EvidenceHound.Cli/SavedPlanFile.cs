using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli;

/// <summary>
/// A saved plan as JSON: <c>hound scope plan --out</c> writes it and <c>hound scope apply &lt;file&gt;</c> reads it back. Reading a file
/// written by this version gives back the same plan, so writing it again gives the same text.
/// </summary>
internal static class SavedPlanFile
{
    /// <summary>Marks a file as a saved plan, and its layout's version.</summary>
    public static readonly string Format = "hound-scope-plan/1";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(SavedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new JsonObject
        {
            ["format"] = Format,
            ["ciso_assistant_url"] = plan.CisoAssistantUrl,
            ["day"] = plan.Day,
            ["scope"] = plan.Scope,
            ["config"] = plan.Config,
            ["inputs_sha256"] = plan.InputsSha256,
            ["root_folder"] = plan.RootFolder,
            ["plan"] = new JsonObject
            {
                ["changes"] = new JsonArray([.. plan.Plan.Changes.Select(c => (JsonNode)new JsonObject
                {
                    ["kind"] = ScopePlanOutput.Kind(c.Kind),
                    ["key"] = c.Key,
                    ["action"] = Snake(c.Action.ToString()),
                    ["fields"] = Fields(c.Fields),
                    ["state"] = c.State,
                    ["desired"] = c.Desired is { } desired ? Object(desired) : null,
                })]),
                ["drift"] = new JsonArray([.. plan.Plan.Drift.Select(d => (JsonNode)new JsonObject
                {
                    ["kind"] = ScopePlanOutput.Kind(d.Kind),
                    ["key"] = d.Key,
                    ["reason"] = Snake(d.Reason.ToString()),
                    ["managed"] = d.Managed,
                    ["fields"] = Fields(d.Fields),
                })]),
            },
            ["compliance"] = new JsonArray([.. plan.Compliance.Select(a => (JsonNode)new JsonObject
            {
                ["audit"] = a.Audit,
                ["groups"] = new JsonArray([.. a.Groups.Select(g => (JsonNode)new JsonObject
                {
                    ["group"] = g.Group,
                    ["name"] = g.Name,
                    ["before"] = g.Before,
                    ["after"] = g.After,
                    ["total"] = g.Total,
                })]),
                ["rows"] = new JsonArray([.. a.Rows.Select(r => (JsonNode)new JsonObject
                {
                    ["requirement"] = r.Requirement,
                    ["title"] = r.Title,
                    ["parent"] = r.Parent,
                    ["old"] = r.Old,
                    ["new"] = r.New,
                    ["why"] = r.Why,
                    ["regresses"] = r.Regresses,
                })]),
            })]),
            ["read"] = new JsonObject([.. plan.Read.Select(r => KeyValuePair.Create(r.Key, (JsonNode?)r.Value))]),
        }.ToJsonString(Indented) + "\n";
    }

    /// <summary>The saved plan in <paramref name="json"/>, or <c>scope.plan_invalid</c> naming <paramref name="source"/> when it is not one.</summary>
    public static Result<SavedPlan> Read(string json, string source)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("empty");
            if (root["format"]?.GetValue<string>() != Format)
            {
                return Invalid(source, $"it is not a saved plan from this version of hound (no \"format\": \"{Format}\")");
            }

            var plan = root["plan"]!;
            return Result.Ok(new SavedPlan(
                Text(root, "ciso_assistant_url"),
                Text(root, "day"),
                Text(root, "scope"),
                root["config"]?.GetValue<string>(),
                Text(root, "inputs_sha256"),
                Text(root, "root_folder"),
                new ScopePlan(
                    [.. plan["changes"]!.AsArray().Select(c => new ScopeChange(
                        Kind(Text(c!, "kind")),
                        Text(c!, "key"),
                        Parse<ScopeAction>(Text(c!, "action")),
                        ReadFields(c!["fields"]!),
                        c!["state"]?.GetValue<string>())
                    {
                        Desired = c["desired"] is { } desired ? ReadObject(desired) : null,
                    })],
                    [.. plan["drift"]!.AsArray().Select(d => new ScopeDrift(
                        Kind(Text(d!, "kind")),
                        Text(d!, "key"),
                        Parse<DriftReason>(Text(d!, "reason")),
                        d!["managed"]!.GetValue<bool>(),
                        ReadFields(d["fields"]!)))]),
                [.. root["compliance"]!.AsArray().Select(a => new AuditImpact(
                    Text(a!, "audit"),
                    [.. a!["groups"]!.AsArray().Select(g => new ImpactGroup(Text(g!, "group"), Text(g!, "name"), Number(g!, "before"), Number(g!, "after"), Number(g!, "total")))],
                    [.. a["rows"]!.AsArray().Select(r => new ImpactRow(
                        Text(r!, "requirement"), Text(r!, "title"), r!["parent"]?.GetValue<string>(), Text(r, "old"), Text(r, "new"), Text(r, "why"), r["regresses"]!.GetValue<bool>()))]))],
                root["read"]!.AsObject().ToDictionary(r => r.Key, r => r.Value?.GetValue<string>(), StringComparer.Ordinal)));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or NullReferenceException or ArgumentException)
        {
            return Invalid(source, exception.Message);
        }
    }

    private static Result<SavedPlan> Invalid(string source, string why) =>
        Result.Fail<SavedPlan>(new Problem("scope.plan_invalid", $"{source} is not a saved scope plan: {why}. Make it again with 'hound scope plan --out'. [scope.plan_invalid]"));

    private static JsonObject Object(ScopeObject o) => new()
    {
        ["kind"] = ScopePlanOutput.Kind(o.Kind),
        ["key"] = o.Key,
        ["fields"] = Map(o.Fields),
        ["initial"] = Map(o.Initial),
        ["files"] = new JsonArray([.. o.Files.Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["sha256"] = f.Sha256 })]),
    };

    private static ScopeObject ReadObject(JsonNode node) =>
        new(Kind(Text(node, "kind")), Text(node, "key"), ReadMap(node["fields"]!))
        {
            Initial = ReadMap(node["initial"]!),
            Files = [.. node["files"]!.AsArray().Select(f => new ScopeFile(Text(f!, "path"), Text(f!, "sha256")))],
        };

    private static JsonObject Map(IReadOnlyDictionary<string, string> fields) =>
        new([.. fields.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))]);

    private static Dictionary<string, string> ReadMap(JsonNode node) =>
        node.AsObject().ToDictionary(f => f.Key, f => f.Value!.GetValue<string>(), StringComparer.Ordinal);

    private static JsonArray Fields(IReadOnlyList<FieldChange> fields) =>
        new([.. fields.Select(f => (JsonNode)new JsonObject { ["field"] = f.Field, ["old"] = f.Old, ["new"] = f.New })]);

    private static FieldChange[] ReadFields(JsonNode node) =>
        [.. node.AsArray().Select(f => new FieldChange(Text(f!, "field"), f!["old"]?.GetValue<string>(), f["new"]?.GetValue<string>()))];

    private static string Text(JsonNode node, string name) => node[name]?.GetValue<string>() ?? throw new FormatException($"'{name}' is missing");

    private static int Number(JsonNode node, string name) => node[name]?.GetValue<int>() ?? throw new FormatException($"'{name}' is missing");

    /// <summary>An enum value from its snake_case name, as <see cref="Snake"/> writes it.</summary>
    /// <summary>A kind by the name the plan writes for it (<see cref="ScopePlanOutput.Kind"/>), so every kind the writer names reads back.</summary>
    private static ScopeKind Kind(string name) =>
        Enum.GetValues<ScopeKind>().FirstOrDefault(k => ScopePlanOutput.Kind(k) == name) is var kind && ScopePlanOutput.Kind(kind) == name
            ? kind
            : throw new FormatException($"'{name}' is not a ScopeKind");

    private static T Parse<T>(string name)
        where T : struct, Enum =>
        Enum.TryParse<T>(name.Replace("_", "", StringComparison.Ordinal), ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new FormatException($"'{name}' is not a {typeof(T).Name}");

    private static string Snake(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));
}
