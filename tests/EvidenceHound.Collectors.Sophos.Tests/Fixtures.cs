using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos.Tests;

/// <summary>The recorded tenant parsed for pure check tests, optionally edited first, and options for it.</summary>
internal static class Fixtures
{
    /// <summary>The time the recorded tenant was read.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    public static SophosData Recorded(Action<Dictionary<string, JsonArray>>? edit = null)
    {
        var items = RecordedSophos.Items();
        edit?.Invoke(items);
        return SophosData.Parse(items["firewalls"], items["admins"], items["roles"]);
    }

    public static SophosOptions Options(Action<Dictionary<string, string>>? edit = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = RecordedSophos.ClientId,
            ["supported_firewall_firmware"] = RecordedSophos.SupportedFirmware,
        };
        edit?.Invoke(settings);
        return SophosOptions.FromSettings(new CollectorSettings(settings)).Value();
    }

    /// <summary>The <paramref name="index"/>th item of <paramref name="list"/>.</summary>
    public static JsonObject At(this Dictionary<string, JsonArray> items, string list, int index) => items[list][index]!.AsObject();
}
