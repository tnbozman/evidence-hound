using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>What a code check reads: the rows of each source it names, in the order of its <c>over</c>, its <c>with</c> settings (parameters filled in) and the run's clock.</summary>
public sealed record CodeCheckInput(IReadOnlyList<JsonArray> Over, IReadOnlyDictionary<string, string> With, DateTimeOffset Now)
{
    /// <summary>The items of the check's <c>history</c> query in each earlier run that kept its evidence, oldest first; empty without history.</summary>
    public IReadOnlyList<EarlierRows> Earlier { get; init; } = [];
}

/// <summary>A query's items as an earlier run saved them, and when that run collected them.</summary>
public sealed record EarlierRows(DateTimeOffset CollectedAt, JsonArray Items);

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
        ["sophos.firmware_upgrade_age"] = FirmwareUpgradeAge,
        ["sophos.super_admin_count"] = SuperAdminCount,
        ["terraform.no_drift"] = TerraformNoDrift,
        ["terraform.tests_pass"] = TerraformTestsPass,
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
    /// No Sophos Central firewall has had a firmware upgrade outstanding for more than <c>with.days</c> days. Reads
    /// <c>over: [firewalls, firmware_upgrades]</c> with <c>history: firmware_upgrades</c>: the upgrade check's answers in this and earlier
    /// runs. Sophos Central gives no release date, so an offer's age counts from the first run of the unbroken series of runs that saw
    /// one for that firewall; counting starts with the first run that keeps evidence. A firewall the upgrade check did not answer for
    /// cannot be assessed.
    /// </summary>
    public static Finding FirmwareUpgradeAge(CodeCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Over is not [var firewalls, var upgrades] || !int.TryParse(input.With.GetValueOrDefault("days"), NumberStyles.None, CultureInfo.InvariantCulture, out var days))
        {
            return new(CheckOutcome.Error, "Unable to assess: sophos.firmware_upgrade_age reads over: [firewalls, firmware_upgrades] with: { days: <number> }.");
        }

        var ids = firewalls.OfType<JsonObject>().Select(f => Text(f["id"])).OfType<string>().ToArray();
        if (ids.Length == 0)
        {
            return new(CheckOutcome.NotApplicable, "Sophos Central manages no firewalls.");
        }

        var now = Offered(upgrades);
        var unanswered = ids.Count(id => !now.ContainsKey(id));
        if (unanswered > 0)
        {
            return new(CheckOutcome.Error, string.Create(CultureInfo.InvariantCulture, $"Unable to assess: the firmware upgrade check gave no answer for {unanswered} of {ids.Length} firewalls."));
        }

        var earlier = input.Earlier.Select(e => (e.CollectedAt, Offers: Offered(e.Items))).ToArray();
        var ages = ids.Where(id => now[id]).Select(id => (input.Now - Since(id, earlier, input.Now)).Days).ToArray();
        var counting = earlier.Length == 0
            ? "Ages count from the first run that saw an offer; this is the first run, so counting starts today."
            : string.Create(CultureInfo.InvariantCulture, $"Ages count from the first run that saw an offer; the earliest run kept is {earlier[0].CollectedAt.UtcDateTime:yyyy-MM-dd}.");
        if (ages.Length == 0)
        {
            return new(CheckOutcome.Pass, string.Create(CultureInfo.InvariantCulture, $"No firewall has a firmware upgrade outstanding ({Count(ids.Length, "firewall")})."));
        }

        var overdue = ages.Count(a => a > days);
        var longest = string.Create(CultureInfo.InvariantCulture, $"the longest {ages.Max()} days");
        return overdue == 0
            ? new(CheckOutcome.Pass, string.Create(CultureInfo.InvariantCulture, $"{ages.Length} of {ids.Length} firewalls {Has(ages.Length)} a firmware upgrade offered, none for more than {days} days ({longest}). {counting}"))
            : new(CheckOutcome.Fail, string.Create(CultureInfo.InvariantCulture, $"{overdue} of {ids.Length} firewalls {Has(overdue)} had a firmware upgrade outstanding for more than {days} days ({longest}); {ages.Length - overdue} more {Has(ages.Length - overdue)} one offered within the window. {counting}"));
    }

    /// <summary>Whether each firewall the upgrade check answered for has any upgrade offered, by firewall ID.</summary>
    private static Dictionary<string, bool> Offered(JsonArray answers) =>
        answers.OfType<JsonObject>()
            .Select(a => (Id: Text(a["id"]), Offered: (a["upgradeToVersion"] as JsonArray ?? []).Count > 0))
            .Where(a => a.Id is not null)
            .GroupBy(a => a.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Any(a => a.Offered), StringComparer.Ordinal);

    /// <summary>When the firewall's current, unbroken run of offers began: walking back from the newest earlier run while each one saw an offer.</summary>
    private static DateTimeOffset Since(string id, (DateTimeOffset CollectedAt, Dictionary<string, bool> Offers)[] earlier, DateTimeOffset now)
    {
        var since = now;
        foreach (var run in earlier.Where(e => e.CollectedAt < now).OrderByDescending(e => e.CollectedAt))
        {
            if (!run.Offers.GetValueOrDefault(id))
            {
                break;
            }

            since = run.CollectedAt;
        }

        return since;
    }

    private static string Has(int count) => count == 1 ? "has" : "have";

    private static string Count(int count, string noun) => string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

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

        var from = Provenance("Plan from", input.With, run);
        if (Text(run["conclusion"]) is var conclusion && conclusion != "success")
        {
            return new(CheckOutcome.Error, $"Unable to assess: the newest run ended {conclusion ?? "without a conclusion"}, so its plan is not trusted. {from}");
        }

        if (Stale(run, input.Now, maxAge))
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

    /// <summary>
    /// The infrastructure's own tests passed: the JUnit report of <c>terraform test -junit-xml</c> (or any JUnit report) an IaC pipeline
    /// published. Reads <c>over: [run, tests]</c>, the pipeline run and the report's test cases as the engine reads XML (<c>@name</c>,
    /// and a <c>failure</c>, <c>error</c> or <c>skipped</c> element when one did not pass), with
    /// <c>with: { max_age: &lt;ISO 8601 duration&gt;, repository: &lt;owner/name&gt;, test: &lt;name, optional&gt; }</c>. With <c>test</c>, only that
    /// test is judged. A failed or errored test fails, naming it and its message. No run, a run that was neither a success nor a
    /// failure (a failing test fails the run, which is a result), a report older than <c>max_age</c>, no report, a missing named test
    /// or only skipped tests cannot be assessed. The finding names the repository, commit and run, read from the run.
    /// </summary>
    public static Finding TerraformTestsPass(CodeCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Over is not [var runs, var cases] || !Profiles.Durations.TryParse(input.With.GetValueOrDefault("max_age") ?? "", out var maxAge))
        {
            return new(CheckOutcome.Error, "Unable to assess: terraform.tests_pass reads over: [run, tests] with: { max_age: <duration such as P2D>, repository: <owner/name>, test: <optional name> }.");
        }

        if (runs.FirstOrDefault() is not JsonObject run)
        {
            return new(CheckOutcome.Error, "Unable to assess: the pipeline has no completed run, so there is no test report to read.");
        }

        var from = Provenance("Tests from", input.With, run);
        if (Text(run["conclusion"]) is var conclusion && conclusion is not ("success" or "failure"))
        {
            return new(CheckOutcome.Error, $"Unable to assess: the newest run ended {conclusion ?? "without a conclusion"}, so its report is not trusted. {from}");
        }

        if (Stale(run, input.Now, maxAge))
        {
            return new(CheckOutcome.Error, $"Unable to assess: the newest test report is older than {input.With["max_age"]}; run the pipeline again. {from}");
        }

        var results = cases.OfType<JsonObject>().Select(TestResult.Of).ToArray();
        if (results.Length == 0)
        {
            return new(CheckOutcome.Error, $"Unable to assess: the run published no test report. {from}");
        }

        if (input.With.GetValueOrDefault("test") is { Length: > 0 } name)
        {
            return results.FirstOrDefault(r => r.Name == name) is { } one
                ? new(one.Outcome, $"{one.Describe()}. {from}")
                : new(CheckOutcome.Error, $"Unable to assess: the test report has no test named {name}. {from}");
        }

        var passed = results.Count(r => r.Outcome == CheckOutcome.Pass);
        var notPassed = results.Where(r => r.Outcome != CheckOutcome.Pass).Select(r => r.Describe()).ToArray();
        var summary = string.Create(CultureInfo.InvariantCulture, $"{passed} of {results.Length} infrastructure tests passed");
        var detail = notPassed.Length == 0 ? "" : $": {string.Join("; ", notPassed)}";
        var outcome = results.Any(r => r.Outcome == CheckOutcome.Fail) ? CheckOutcome.Fail : passed == results.Length ? CheckOutcome.Pass : CheckOutcome.Error;
        return new(outcome, $"{(outcome == CheckOutcome.Error ? "Unable to assess: " : "")}{summary}{detail}. {from}");
    }

    /// <summary>One JUnit test case: passed, failed (a failure or an error element) or skipped, which cannot be assessed.</summary>
    private sealed record TestResult(string Name, CheckOutcome Outcome, string Detail)
    {
        public static TestResult Of(JsonObject test)
        {
            var name = Text(test["@name"]) ?? "unnamed";
            return test["failure"] is { } failure ? new(name, CheckOutcome.Fail, $"failed: {Message(failure)}")
                : test["error"] is { } error ? new(name, CheckOutcome.Fail, $"ended in error: {Message(error)}")
                : test["skipped"] is { } skipped ? new(name, CheckOutcome.Error, $"was skipped: {Message(skipped)}")
                : new(name, CheckOutcome.Pass, "passed");
        }

        public string Describe() => $"{Name} {Detail}";

        private static string Message(JsonNode node) => (Text(node["@message"]) ?? Text(node) ?? "no message").Trim();
    }

    /// <summary>Resource changes whose actions are more than no-op or read.</summary>
    private static IEnumerable<(string Address, string[] Actions)> Changes(JsonNode? list) =>
        (list as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => (Address: Text(c["address"]) ?? "unknown", Actions: (c["change"]?["actions"] as JsonArray ?? []).Select(Text).OfType<string>().ToArray()))
            .Where(c => c.Actions.Any(a => a is not ("no-op" or "read")));

    /// <summary>Which repository, commit and pipeline run a finding was made from, read from the run, never from the artefact's name.</summary>
    private static string Provenance(string what, IReadOnlyDictionary<string, string> with, JsonObject run) =>
        string.Create(CultureInfo.InvariantCulture, $"{what} {with.GetValueOrDefault("repository") ?? "the IaC repository"} at commit {Text(run["head_sha"]) ?? "unknown"}, run {Scalar(run["id"]) ?? "unknown"}.");

    private static bool Stale(JsonObject run, DateTimeOffset now, TimeSpan maxAge) =>
        !DateTimeOffset.TryParse(Text(run["updated_at"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updated) || now - updated > maxAge;

    private static string? Scalar(JsonNode? node) => node is JsonValue value ? value.ToString() : null;

    private static bool IsSuperAdminName(string name) =>
        string.Equals(name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal), "superadmin", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
