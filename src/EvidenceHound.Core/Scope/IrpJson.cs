using System.Text.Json;
using System.Text.Json.Nodes;

namespace EvidenceHound.Core.Scope;

/// <summary>The incident response plan as JSON: every value as <c>{"value", "source"}</c>, as the SSP writes it.</summary>
public static class IrpJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(IrpDocument plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new JsonObject
        {
            ["programme"] = plan.Programme,
            ["gaps"] = new JsonArray([.. plan.Gaps.Select(g => new JsonObject { ["requirement"] = g.Requirement, ["reason"] = g.Reason })]),
            ["version"] = Value(plan.Version),
            ["contacts"] = new JsonArray([.. plan.Contacts.Select(c => new JsonObject { ["role"] = c.Role, ["holder"] = Value(c.Holder) })]),
            ["systems"] = new JsonArray([.. plan.Systems.Select(s => new JsonObject
            {
                ["name"] = Value(s.Name),
                ["data"] = new JsonArray([.. s.Data.Select(Value)]),
                ["recovery"] = Value(s.Recovery),
            })]),
            ["parties"] = new JsonArray([.. plan.Parties.Select(p => new JsonObject
            {
                ["kind"] = p.Kind,
                ["name"] = Value(p.Name),
                ["contacts"] = new JsonArray([.. p.Contacts.Select(Value)]),
                ["clauses"] = new JsonArray([.. p.Clauses.Select(Value)]),
            })]),
            ["reports"] = new JsonArray([.. plan.Reports.Select(r => new JsonObject
            {
                ["name"] = Value(r.Name),
                ["report_to"] = Value(r.ReportTo),
                ["within"] = Value(r.Within),
                ["when"] = Value(r.Trigger),
                ["reference"] = Value(r.Reference),
            })]),
            ["sections"] = new JsonArray([.. plan.Sections.Select(s => new JsonObject { ["name"] = s.Name, ["file"] = Value(s.Value) })]),
        }.ToJsonString(Indented) + "\n";
    }

    private static JsonObject Value(SspValue value) => new()
    {
        ["value"] = value.Value,
        ["source"] = new JsonObject { ["kind"] = value.Source.Kind, ["reference"] = value.Source.Reference },
    };
}
