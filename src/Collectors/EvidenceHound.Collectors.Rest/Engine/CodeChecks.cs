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
        ["terraform.no_drift"] = TerraformNoDrift,
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

    /// <summary>
    /// The infrastructure matches its code: the plan JSON an IaC pipeline published (<c>terraform plan -refresh-only -json</c> or a
    /// normal plan) lists no drift and no change. Reads <c>over: [run, plan]</c>, the pipeline run and its plan, with
    /// <c>with: { max_age: &lt;ISO 8601 duration&gt;, repository: &lt;owner/name&gt; }</c>. No run, a run that did not succeed, a plan older
    /// than <c>max_age</c> or no plan cannot be assessed. The finding names the repository, commit and run it was made from, read from the run.
    /// </summary>
    public static Finding TerraformNoDrift(CodeCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Over is not [var runs, var plans] || !Profiles.Durations.TryParse(input.With.GetValueOrDefault("max_age") ?? "", out var maxAge))
        {
            return new(CheckOutcome.Error, "Unable to assess: terraform.no_drift reads over: [run, plan] with: { max_age: <duration such as P2D>, repository: <owner/name> }.");
        }

        if (runs.FirstOrDefault() is not JsonObject run)
        {
            return new(CheckOutcome.Error, "Unable to assess: the pipeline has no completed run, so there is no plan to read.");
        }

        var from = string.Create(
            CultureInfo.InvariantCulture,
            $"Plan from {input.With.GetValueOrDefault("repository") ?? "the IaC repository"} at commit {Text(run["head_sha"]) ?? "unknown"}, run {Scalar(run["id"]) ?? "unknown"}.");
        if (Text(run["conclusion"]) is var conclusion && conclusion != "success")
        {
            return new(CheckOutcome.Error, $"Unable to assess: the newest run ended {conclusion ?? "without a conclusion"}, so its plan is not trusted. {from}");
        }

        if (!DateTimeOffset.TryParse(Text(run["updated_at"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updated) || input.Now - updated > maxAge)
        {
            return new(CheckOutcome.Error, $"Unable to assess: the newest plan is older than {input.With["max_age"]}; run the pipeline again. {from}");
        }

        if (plans.FirstOrDefault() is not JsonObject plan)
        {
            return new(CheckOutcome.Error, $"Unable to assess: the run published no plan. {from}");
        }

        var changed = Changes(plan["resource_drift"]).Concat(Changes(plan["resource_changes"]))
            .GroupBy(c => c.Address, StringComparer.Ordinal)
            .Select(g => $"{g.Key} ({string.Join(", ", g.First().Actions)})")
            .ToArray();
        return changed.Length == 0
            ? new(CheckOutcome.Pass, $"No drift: the infrastructure matches its code. {from}")
            : new(CheckOutcome.Fail, string.Create(CultureInfo.InvariantCulture, $"{changed.Length} {(changed.Length == 1 ? "resource differs" : "resources differ")} from the infrastructure code: {string.Join("; ", changed)}. {from}"));
    }

    /// <summary>Resource changes whose actions are more than no-op or read.</summary>
    private static IEnumerable<(string Address, string[] Actions)> Changes(JsonNode? list) =>
        (list as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => (Address: Text(c["address"]) ?? "unknown", Actions: (c["change"]?["actions"] as JsonArray ?? []).Select(Text).OfType<string>().ToArray()))
            .Where(c => c.Actions.Any(a => a is not ("no-op" or "read")));

    private static string? Scalar(JsonNode? node) => node is JsonValue value ? value.ToString() : null;

    private static bool IsSuperAdminName(string name) =>
        string.Equals(name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal), "superadmin", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
