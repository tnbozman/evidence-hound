using System.Text.Json.Nodes;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// The scope names a built-in role by its code (<c>BI-RL-ADM</c>), but CISO Assistant's roles list shows a built-in role's display name in
/// <c>name</c> ("Administrator"; <c>BI-RL-AUD</c> is "Reader"), translated to the request's language. Requests ask for English, and this
/// maps the English names of v4.3.4's built-in roles back to their codes. A custom role keeps its own name.
/// </summary>
internal static class RoleCodes
{
    private static readonly Dictionary<string, string> BuiltinByName = new(StringComparer.Ordinal)
    {
        ["Administrator"] = "BI-RL-ADM",
        ["Domain manager"] = "BI-RL-DMA",
        ["Analyst"] = "BI-RL-ANA",
        ["Approver"] = "BI-RL-APP",
        ["Reader"] = "BI-RL-AUD",
        ["Baseline reader"] = "BI-RL-BSL",
        ["Third-party respondent"] = "BI-RL-TPR",
        ["Respondent"] = "BI-RL-ADE",
        ["Technical tester"] = "BI-RL-TST",
        ["User creator"] = "BI-RL-UCR",
    };

    /// <summary>A role's code as the scope writes it: a built-in role's <c>BI-RL-…</c> code, or a custom role's name.</summary>
    public static string? Of(JsonNode? role)
    {
        var name = role?["name"] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
        var builtin = role?["builtin"] is JsonValue flag && flag.TryGetValue<bool>(out var isBuiltin) && isBuiltin;
        return builtin && name is not null && BuiltinByName.TryGetValue(name, out var code) ? code : name;
    }

    /// <summary>Each role's code by its ID.</summary>
    public static Dictionary<string, string> ById(IEnumerable<JsonNode> roles, Func<JsonNode, string> id) =>
        roles.Where(r => Of(r) is not null).ToDictionary(id, r => Of(r)!, StringComparer.Ordinal);
}
