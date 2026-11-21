using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>The recorded tenant parsed for pure check tests, optionally edited first, and options for it.</summary>
internal static class Tenants
{
    public static TenantData Recorded(Action<Dictionary<string, JsonArray>>? edit = null)
    {
        var items = RecordedTenant.Items();
        edit?.Invoke(items);
        return TenantData.Parse(items.ToDictionary(pair => pair.Key, pair => (JsonArray?)pair.Value, StringComparer.Ordinal));
    }

    public static M365Options Options(Action<Dictionary<string, string>>? edit = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant_id"] = RecordedTenant.TenantId,
            ["client_id"] = RecordedTenant.ClientId,
            ["break_glass_accounts"] = RecordedTenant.BreakGlass,
            ["supported_windows_builds"] = RecordedTenant.SupportedBuilds,
        };
        edit?.Invoke(settings);
        return M365Options.FromSettings(new CollectorSettings(settings)).Value();
    }

    /// <summary>The item in <paramref name="collection"/> whose <c>id</c> is <paramref name="id"/>.</summary>
    public static JsonObject Item(this Dictionary<string, JsonArray> items, string collection, string id) =>
        items[collection].Single(i => i!["id"]!.GetValue<string>() == id)!.AsObject();

    public static JsonObject Policy(this Dictionary<string, JsonArray> items, string id) => items.Item("conditional-access-policies", id);

    public static JsonArray Strings(params string[] values) => [.. values.Select(v => (JsonNode)JsonValue.Create(v))];
}
