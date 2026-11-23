using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>
/// s09-33: the terraform-drift profiles read the plan JSON an IaC pipeline published as an artefact; hound never runs Terraform.
/// The library profile's own test cases replay a fictional GitHub Actions API.
/// </summary>
public sealed class TerraformDriftProfileTests
{
    private const string DriftCheck = "infrastructure.drift.none";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> With = new(StringComparer.Ordinal) { ["max_age"] = "P2D", ["repository"] = "example/grc-infra" };

    [Fact]
    public void No_drift_passes()
    {
        var finding = Check(Run(), Plan(drift: [], changes: [Change("example_instance.grc", "no-op")]));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldStartWith("No drift:");
    }

    [Fact]
    public void Drift_fails_naming_each_changed_resource()
    {
        var finding = Check(Run(), Plan(drift: [Change("oci_core_instance.grc", "update")], changes: [Change("oci_core_security_list.grc", "delete", "create"), Change("oci_core_vcn.grc", "no-op")]));

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("2 resources differ");
        finding.Observation.ShouldContain("oci_core_instance.grc (update)");
        finding.Observation.ShouldContain("oci_core_security_list.grc (delete, create)");
        finding.Observation.ShouldNotContain("oci_core_vcn.grc");
    }

    [Fact]
    public void Missing_failed_or_stale_artefact_is_unable_to_assess()
    {
        var missing = Check(Run(), []);
        var failed = Check(Run(conclusion: "failure"), Plan(drift: [], changes: []));
        var stale = Check(Run(updated: Now.AddDays(-3)), Plan(drift: [], changes: []));
        var noRun = Check([], Plan(drift: [], changes: []));

        new[] { missing, failed, stale, noRun }.ShouldAllBe(f => f.Outcome == CheckOutcome.Error);
        missing.Observation.ShouldContain("no plan");
        failed.Observation.ShouldContain("ended failure");
        stale.Observation.ShouldContain("older than P2D");
        noRun.Observation.ShouldContain("no completed run");
    }

    [Fact]
    public void Evidence_records_repository_commit_and_run()
    {
        var pass = Check(Run(), Plan(drift: [], changes: []));
        var fail = Check(Run(), Plan(drift: [Change("oci_core_instance.grc", "update")], changes: []));

        foreach (var finding in new[] { pass, fail })
        {
            finding.Observation.ShouldContain("example/grc-infra");
            finding.Observation.ShouldContain("commit 0123456789abcdef0123456789abcdef01234567");
            finding.Observation.ShouldContain("run 7001");
        }
    }

    [Fact]
    public async Task Library_profiles_replay_each_case()
    {
        foreach (var name in new[] { "terraform-drift", "terraform-drift-trigger" })
        {
            var cases = (await ProfileTestRunner.RunAsync(Profiles.LibraryFolder, name, TestContext.Current.CancellationToken)).Value();

            cases.Where(c => !c.Passed).Select(c => c.Name).ShouldBeEmpty();
            var actual = cases.ToDictionary(c => c.Name, c => c.Checks.Single(k => k.CheckId == DriftCheck).Actual);
            if (name == "terraform-drift")
            {
                actual.ShouldBe(new Dictionary<string, string> { ["no-drift"] = "pass", ["drift"] = "fail", ["missing-artefact"] = "unable to assess" }, ignoreOrder: true);
            }
            else
            {
                actual.ShouldBe(new Dictionary<string, string> { ["triggered"] = "fail", ["writes-off"] = "unable to assess" }, ignoreOrder: true);
            }
        }
    }

    private static Finding Check(JsonArray run, JsonArray plan) => CodeChecks.All["terraform.no_drift"](new CodeCheckInput([run, plan], With, Now));

    private static JsonArray Run(string conclusion = "success", DateTimeOffset? updated = null) =>
    [
        new JsonObject
        {
            ["id"] = 7001,
            ["head_sha"] = "0123456789abcdef0123456789abcdef01234567",
            ["head_branch"] = "main",
            ["status"] = "completed",
            ["conclusion"] = conclusion,
            ["updated_at"] = (updated ?? Now.AddHours(-3)).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        },
    ];

    private static JsonArray Plan(JsonNode[] drift, JsonNode[] changes)
    {
        var plan = new JsonObject { ["format_version"] = "1.2", ["resource_changes"] = new JsonArray(changes) };
        if (drift.Length > 0)
        {
            plan["resource_drift"] = new JsonArray(drift);
        }

        return [plan];
    }

    private static JsonObject Change(string address, params string[] actions) =>
        new() { ["address"] = address, ["change"] = new JsonObject { ["actions"] = new JsonArray([.. actions.Select(a => (JsonNode)a)]) } };
}
