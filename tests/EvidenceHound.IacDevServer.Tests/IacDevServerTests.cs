using System.Text.RegularExpressions;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.IacDevServer.Tests;

/// <summary>The fictional Example Pty Ltd IaC pipeline (s09-56), read by the terraform-tests, grc-infra-controls and terraform-drift profiles.</summary>
public sealed partial class IacDevServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Profiles => Path.Combine(AppContext.BaseDirectory, "profiles");

    private static readonly string[] AllProfiles = ["terraform-tests", "grc-infra-controls", "terraform-drift"];

    [Fact]
    public async Task Compliant_pipeline_passes_both_profiles()
    {
        using var server = DevIacServer.Start(IacDevPipeline.Compliant, Now);

        foreach (var profile in AllProfiles)
        {
            var checks = (await CollectAsync(server, profile)).Value().Checks;

            checks.ShouldNotBeEmpty(profile);
            checks.Where(c => c.Outcome != CheckOutcome.Pass).Select(c => $"{profile} {c.CheckId.Value}: {c.Observation}").ShouldBeEmpty();
        }

        server.Requests.ShouldAllBe(r => r.Status == 200);
    }

    [Fact]
    public void The_report_has_one_test_per_run_block_of_the_tutorials_control_tests()
    {
        var runs = RunBlock().Matches(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "infra", "controls.tftest.hcl")))
            .Select(m => m.Groups[1].Value);

        IacDevPipeline.Compliant.ControlTests.Select(t => t.Name).ShouldBe(runs, ignoreOrder: true);
    }

    [Fact]
    public async Task Each_deviation_fails_its_named_check()
    {
        IacDevPipeline.Deviations.Count.ShouldBeGreaterThanOrEqualTo(2);
        foreach (var deviation in IacDevPipeline.Deviations)
        {
            using var server = DevIacServer.Start(IacDevPipeline.Compliant.With(deviation), Now);

            foreach (var profile in AllProfiles)
            {
                foreach (var check in (await CollectAsync(server, profile)).Value().Checks)
                {
                    var expected = deviation.Fails.TryGetValue(check.CheckId.Value, out var named) ? named : CheckOutcome.Pass;
                    check.Outcome.ShouldBe(expected, $"{deviation.Name}: {profile} {check.CheckId.Value}: {check.Observation}");
                }
            }
        }
    }

    [Fact]
    public async Task A_given_report_is_served_as_the_artefact()
    {
        const string report = """
            <?xml version="1.0" encoding="UTF-8"?><testsuites>
              <testsuite name="tests/controls.tftest.hcl" tests="2" skipped="0" failures="1" errors="0">
                <testcase name="vm_has_no_public_ip" classname="tests/controls.tftest.hcl"><failure message="Test assertion failed">The VM has a public IP.</failure></testcase>
                <testcase name="storage_encrypted" classname="tests/controls.tftest.hcl"></testcase>
              </testsuite>
            </testsuites>
            """;
        using var server = DevIacServer.Start(IacDevPipeline.Compliant.WithReport(report), Now);

        var checks = (await CollectAsync(server, "grc-infra-controls")).Value().Checks.ToDictionary(c => c.CheckId.Value);

        checks["infrastructure.controls.vm_has_no_public_ip"].Outcome.ShouldBe(CheckOutcome.Fail);
        checks["infrastructure.controls.storage_encrypted"].Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public async Task Runs_are_dated_from_the_clock()
    {
        var later = Now.AddDays(30);
        using var server = DevIacServer.Start(IacDevPipeline.Compliant, later);

        var checks = (await CollectAsync(server, "terraform-drift", later)).Value().Checks;

        checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
    }

    [GeneratedRegex(@"^run ""([a-z0-9_]+)""", RegexOptions.Multiline)]
    private static partial Regex RunBlock();

    private static Task<Result<CollectionResult>> CollectAsync(DevIacServer server, string profile, DateTimeOffset? now = null)
    {
        var settings = IacDevPipeline.Settings(server.Url, profile);
        settings["profiles_path"] = Profiles;
        var secrets = new Dictionary<string, Secret>(StringComparer.Ordinal) { ["token"] = new(IacDevPipeline.Token) };
        var context = new CollectorContext(profile, new CollectorSettings(settings), secrets, new FixedClock(now ?? Now), NullLogger.Instance, server.HttpClients);
        return new RestCollector().CollectAsync(context, Token);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
