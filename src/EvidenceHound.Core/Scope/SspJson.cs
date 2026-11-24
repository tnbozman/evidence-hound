using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Core.Scope;

/// <summary>The plan as JSON: every value as <c>{"value", "source": {"kind", "reference"}}</c>, so each one can be traced. Output is indented and stable.</summary>
public static class SspJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(SspDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new JsonObject
        {
            ["programme"] = document.Programme,
            ["gaps"] = new JsonArray([.. document.Gaps.Select(g => new JsonObject { ["requirement"] = g.Requirement, ["reason"] = g.Reason })]),
            ["profile"] = document.Profile,
            ["system_name"] = Value(document.SystemName),
            ["version"] = Value(document.Version),
            ["framework"] = Value(document.Framework),
            ["audit"] = Value(document.Audit),
            ["perimeter"] = Value(document.Perimeter),
            ["classification"] = Value(document.Classification),
            ["operational_status"] = Value(document.OperationalStatus),
            ["system_type"] = Value(document.SystemType),
            ["roles"] = new JsonArray([.. document.Roles.Select(r => new JsonObject { ["role"] = r.Role, ["holder"] = Value(r.Holder) })]),
            ["inventory"] = new JsonArray([.. document.Inventory.Select(s => new JsonObject
            {
                ["name"] = Value(s.Name), ["type"] = Value(s.Type), ["owner"] = Value(s.Owner), ["description"] = Value(s.Description),
            })]),
            ["external_services"] = new JsonArray([.. document.ExternalServices.Select(s => new JsonObject
            {
                ["system"] = Value(s.System), ["solution"] = Value(s.Solution), ["supplier"] = Value(s.Supplier), ["criticality"] = Value(s.Criticality),
            })]),
            ["controls"] = new JsonArray([.. document.Controls.Select(c => new JsonObject
            {
                ["ref_id"] = Value(c.RefId), ["name"] = Value(c.Name), ["owner"] = Value(c.Owner), ["status"] = Value(c.Status), ["eta"] = Value(c.Eta), ["description"] = Value(c.Description),
            })]),
            ["requirements"] = new JsonArray([.. document.Requirements.Select(r => new JsonObject
            {
                ["ref"] = Value(r.Ref), ["result"] = Value(r.Result), ["observation"] = Value(r.Observation), ["evidence"] = Value(r.Evidence), ["controls"] = Value(r.Controls),
                ["status"] = Value(r.Status), ["statement"] = Value(r.Statement), ["responsible"] = Value(r.Responsible),
                ["gaps"] = new JsonArray([.. r.Gaps.Select(g => JsonValue.Create(g))]),
            })]),
            ["documents"] = new JsonArray([.. document.Documents.Select(Value)]),
            ["sections"] = new JsonArray([.. document.Sections.Select(s => new JsonObject { ["name"] = s.Name, ["file"] = Value(s.Value) })]),
            ["annex"] = new JsonArray([.. document.Annex.Select(a => new JsonObject
            {
                ["control"] = a.Control, ["title"] = a.Title, ["listed_under"] = new JsonArray([.. a.Parents.Select(p => JsonValue.Create(p))]),
                ["applicability"] = Value(a.Applicability), ["status"] = Value(a.Status), ["statement"] = Value(a.Statement),
                ["responsible"] = Value(a.Responsible), ["evidence"] = Value(a.Evidence), ["poam"] = a.Poam,
            })]),
        }.ToJsonString(Indented) + "\n";
    }

    /// <summary>The POA&amp;M as JSON, each value with its source.</summary>
    public static string Write(IReadOnlyList<PoamItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new JsonObject
        {
            ["items"] = new JsonArray([.. items.Select(i => new JsonObject
            {
                ["requirement"] = Value(i.Requirement), ["status"] = Value(i.Status), ["control"] = Value(i.Control), ["owner"] = Value(i.Owner),
                ["eta"] = Value(i.Eta), ["control_status"] = Value(i.ControlStatus),
            })]),
        }.ToJsonString(Indented) + "\n";
    }

    private static JsonObject Value(SspValue value) => new()
    {
        ["value"] = value.Value,
        ["source"] = new JsonObject { ["kind"] = value.Source.Kind, ["reference"] = value.Source.Reference },
    };
}
