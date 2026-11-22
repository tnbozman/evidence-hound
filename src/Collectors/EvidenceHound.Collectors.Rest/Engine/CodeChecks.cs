using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>What a code check reads: the rows of each source it names, in the order of its <c>over</c>, its <c>with</c> settings (parameters filled in) and the run's clock.</summary>
public sealed record CodeCheckInput(IReadOnlyList<JsonArray> Over, IReadOnlyDictionary<string, string> With, DateTimeOffset Now);

/// <summary>A code check: a pure function from its input to a finding.</summary>
public delegate Finding CodeCheck(CodeCheckInput input);

/// <summary>
/// The code checks built into hound, by the name a profile gives in <c>code:</c>. For logic that is not "test a field of each row".
/// Nothing is loaded at runtime: a new code check is a reviewed, tested change to this file.
/// </summary>
public static class CodeChecks
{
    public static IReadOnlyDictionary<string, CodeCheck> All { get; } = new Dictionary<string, CodeCheck>(StringComparer.Ordinal)
    {
        ["sophos.super_admin_count"] = SuperAdminCount,
    };

    public static IReadOnlyList<string> Names { get; } = [.. All.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Sophos Central admins holding the predefined Super Admin role, at most <c>with.max</c>. Reads <c>over: [admins, roles]</c>.
    /// A custom role named Super Admin does not count; a tenant listing no predefined Super Admin role cannot be assessed.
    /// </summary>
    public static Finding SuperAdminCount(CodeCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Over is not [var admins, var roles] || !int.TryParse(input.With.GetValueOrDefault("max"), NumberStyles.None, CultureInfo.InvariantCulture, out var max))
        {
            return new(CheckOutcome.Error, "Unable to assess: sophos.super_admin_count reads over: [admins, roles] with: { max: <number> }.");
        }

        var superAdmin = roles.OfType<JsonObject>()
            .Where(r => Text(r["type"]) == "predefined" && IsSuperAdminName(Text(r["name"]) ?? ""))
            .Select(r => Text(r["id"]))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (superAdmin.Count == 0)
        {
            return new(CheckOutcome.Error, "Unable to assess: Sophos Central listed no predefined Super Admin role, so Super Admins could not be counted.");
        }

        var count = admins.OfType<JsonObject>().Count(a => (a["roleAssignments"] as JsonArray ?? []).OfType<JsonObject>().Any(r => Text(r["roleId"]) is { } id && superAdmin.Contains(id)));
        var holds = count == 1 ? "holds" : "hold";
        var observation = string.Create(CultureInfo.InvariantCulture, $"{count} of {admins.Count} Sophos Central admins {holds} Super Admin; at most {max} allowed.");
        return new(count <= max ? CheckOutcome.Pass : CheckOutcome.Fail, observation);
    }

    private static bool IsSuperAdminName(string name) =>
        string.Equals(name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal), "superadmin", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
