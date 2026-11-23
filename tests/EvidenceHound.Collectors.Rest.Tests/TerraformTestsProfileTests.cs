using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>
/// s09-53: the terraform-tests profile reads the JUnit report of <c>terraform test</c> an IaC pipeline published as an artefact;
/// hound never runs Terraform. Each test case is a row as the engine reads the XML: attributes as <c>@name</c>, and a
/// <c>failure</c>, <c>error</c> or <c>skipped</c> element when the test did not pass.
/// </summary>
public sealed class TerraformTestsProfileTests
{
    internal const string TestsCheck = "infrastructure.tests.pass";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void All_tests_passing_passes()
    {
        var finding = Check(Run(), [Passed("vm_has_no_public_ip"), Passed("backups_retention_locked")]);

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldStartWith("2 of 2 infrastructure tests passed");
        finding.Observation.ShouldContain("example/grc-infra");
        finding.Observation.ShouldContain("commit 0123456789abcdef0123456789abcdef01234567");
        finding.Observation.ShouldContain("run 7101");
    }

    [Fact]
    public void A_failed_test_fails_naming_it_and_its_message()
    {
        var finding = Check(
            Run(),
            [
                Passed("vm_has_no_public_ip"),
                Failed("https_only_from_the_office", "failure", "https_only_from_the_office: the office may reach the VM on TCP 443 only."),
                Failed("vpn_routes_only_the_office", "error", "Encountered an error"),
                Skipped("storage_encrypted"),
            ]);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("1 of 4 infrastructure tests passed");
        finding.Observation.ShouldContain("https_only_from_the_office failed: https_only_from_the_office: the office may reach the VM on TCP 443 only.");
        finding.Observation.ShouldContain("vpn_routes_only_the_office ended in error: Encountered an error");
        finding.Observation.ShouldContain("storage_encrypted was skipped");
        finding.Observation.ShouldNotContain("vm_has_no_public_ip");
    }

    [Fact]
    public void Missing_failed_or_stale_report_is_unable_to_assess()
    {
        var missing = Check(Run(), []);
        var failed = Check(Run(conclusion: "cancelled"), [Passed("vm_has_no_public_ip")]);
        var stale = Check(Run(updated: Now.AddDays(-3)), [Passed("vm_has_no_public_ip")]);
        var noRun = Check([], [Passed("vm_has_no_public_ip")]);
        var onlySkipped = Check(Run(), [Skipped("vm_has_no_public_ip")]);

        new[] { missing, failed, stale, noRun, onlySkipped }.ShouldAllBe(f => f.Outcome == CheckOutcome.Error);
        missing.Observation.ShouldContain("no test report");
        failed.Observation.ShouldContain("ended cancelled");
        stale.Observation.ShouldContain("older than P2D");
        noRun.Observation.ShouldContain("no completed run");
        onlySkipped.Observation.ShouldContain("vm_has_no_public_ip was skipped");
    }

    [Fact]
    public void A_failing_pipeline_run_still_reports_its_tests()
    {
        // terraform test exits non-zero when a test fails, so the run that published the report ends "failure": that is the result, not a broken pipeline.
        var finding = Check(Run(conclusion: "failure"), [Failed("vm_has_no_public_ip", "failure", "vm_has_no_public_ip: the GRC platform's VM must not have a public IP address.")]);

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("vm_has_no_public_ip failed");
    }

    [Fact]
    public void A_named_test_is_checked_alone_and_absent_is_unable_to_assess()
    {
        static JsonNode[] Report() => [Passed("vm_has_no_public_ip"), Failed("https_only_from_the_office", "failure", "only 443")];

        var alone = Check(Run(), Report(), test: "vm_has_no_public_ip");
        var failing = Check(Run(), Report(), test: "https_only_from_the_office");
        var absent = Check(Run(), Report(), test: "backups_retention_locked");

        alone.Outcome.ShouldBe(CheckOutcome.Pass);
        alone.Observation.ShouldStartWith("vm_has_no_public_ip passed");
        failing.Outcome.ShouldBe(CheckOutcome.Fail);
        failing.Observation.ShouldStartWith("https_only_from_the_office failed: only 443");
        absent.Outcome.ShouldBe(CheckOutcome.Error);
        absent.Observation.ShouldContain("has no test named backups_retention_locked");
    }

    [Fact]
    public async Task Library_profile_replays_each_case()
    {
        var cases = (await ProfileTestRunner.RunAsync(Profiles.LibraryFolder, "terraform-tests", TestContext.Current.CancellationToken)).Value();

        cases.Where(c => !c.Passed).Select(c => c.Name).ShouldBeEmpty();
        cases.ToDictionary(c => c.Name, c => c.Checks.Single(k => k.CheckId == TestsCheck).Actual)
            .ShouldBe(new Dictionary<string, string> { ["all-pass"] = "pass", ["one-fails"] = "fail", ["missing-report"] = "unable_to_assess" }, ignoreOrder: true);
    }

    private static Finding Check(JsonArray run, JsonNode[] tests, string? test = null)
    {
        var with = new Dictionary<string, string>(StringComparer.Ordinal) { ["max_age"] = "P2D", ["repository"] = "example/grc-infra" };
        if (test is not null)
        {
            with["test"] = test;
        }

        return CodeChecks.All["terraform.tests_pass"](new CodeCheckInput([run, new JsonArray(tests)], with, Now));
    }

    private static JsonArray Run(string conclusion = "success", DateTimeOffset? updated = null) =>
    [
        new JsonObject
        {
            ["id"] = 7101,
            ["head_sha"] = "0123456789abcdef0123456789abcdef01234567",
            ["status"] = "completed",
            ["conclusion"] = conclusion,
            ["updated_at"] = (updated ?? Now.AddHours(-3)).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        },
    ];

    private static JsonObject Passed(string name) => new() { ["@name"] = name, ["@classname"] = "tests/controls.tftest.hcl" };

    private static JsonObject Failed(string name, string element, string message) =>
        new() { ["@name"] = name, ["@classname"] = "tests/controls.tftest.hcl", [element] = new JsonObject { ["@message"] = message, ["#text"] = "detail" } };

    private static JsonObject Skipped(string name) =>
        new() { ["@name"] = name, ["@classname"] = "tests/controls.tftest.hcl", ["skipped"] = new JsonObject { ["@message"] = "Testcase skipped due to a previous testcase error" } };
}
