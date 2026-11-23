using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary><c>hound profile test</c>: a profile run offline against its recordings, each check's expected result against its actual one.</summary>
public sealed class ProfileTestRunnerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("hound-profile-tests-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Library_example_profile_passes_its_tests()
    {
        CopyExample(withTests: true);

        var cases = (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Value();

        cases.Select(c => c.Name).ShouldBe(["all-pass", "roles-unreadable"]);
        cases.ShouldAllBe(c => c.Passed);
    }

    [Fact]
    public async Task Reports_expected_and_actual_per_check()
    {
        CopyExample(withTests: true);
        var allPass = Path.Combine(_folder, "example.tests", "all-pass.yaml");
        await File.WriteAllTextAsync(allPass, (await File.ReadAllTextAsync(allPass, Token)).Replace("example.device.os_supported: pass", "example.device.os_supported: fail", StringComparison.Ordinal), Token);

        var cases = (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Value();

        var failed = cases.Single(c => c.Name == "all-pass");
        failed.Passed.ShouldBeFalse();
        var check = failed.Checks.Single(c => !c.Passed);
        (check.CheckId, check.Expected, check.Actual).ShouldBe(("example.device.os_supported", "fail", "pass"));
        check.Observation.ShouldStartWith("3 of 3 devices");
        failed.Checks.Count(c => c.Passed).ShouldBe(3);
    }

    [Fact]
    public async Task Request_without_a_recording_is_reported()
    {
        CopyExample(withTests: true);
        var allPass = Path.Combine(_folder, "example.tests", "all-pass.yaml");
        await File.WriteAllLinesAsync(allPass, (await File.ReadAllLinesAsync(allPass, Token)).Where(l => !l.Contains("/roles", StringComparison.Ordinal)), Token);

        var failed = (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Value().Single(c => c.Name == "all-pass");

        failed.Passed.ShouldBeFalse();
        failed.Unrecorded.ShouldBe(["GET /v1/orgs/org-0001/roles"]);
    }

    [Fact]
    public async Task Unknown_check_in_expect_is_a_problem()
    {
        CopyExample(withTests: true);
        var allPass = Path.Combine(_folder, "example.tests", "all-pass.yaml");
        await File.AppendAllTextAsync(allPass, "  example.device.not_a_check: pass\n", Token);

        var problems = (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Problems();

        problems.ShouldHaveSingleItem().Code.ShouldBe("profile.test_invalid");
        problems[0].Message.ShouldContain("example.device.not_a_check");
    }

    [Fact]
    public async Task Profile_without_tests_cannot_be_tested()
    {
        CopyExample(withTests: false);

        (await ProfileTestRunner.RunAsync(_folder, "example", Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("profile.tests_missing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_without_tests_warns_or_fails_by_setting(bool required)
    {
        CopyExample(withTests: false);
        using var api = RecordedExample.Start();
        var settings = api.Settings(_folder);
        if (required)
        {
            settings["require_profile_tests"] = "true";
        }

        var logger = new ListLogger();
        var result = (await new RestCollector().CollectAsync(
            new CollectorContext("example-rest", new CollectorSettings(settings), Secrets(), new Clock(), logger, RecordedExample.HttpClients), Token)).Value();

        if (required)
        {
            result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error);
            result.Checks[0].Observation.ShouldContain("profile.tests_missing");
        }
        else
        {
            result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
            logger.Warnings.ShouldHaveSingleItem().ShouldContain("example.tests");
        }
    }

    private void CopyExample(bool withTests)
    {
        File.Copy(Path.Combine(Profiles.LibraryFolder, "example.yaml"), Path.Combine(_folder, "example.yaml"));
        if (withTests)
        {
            var source = Path.Combine(Profiles.LibraryFolder, "example.tests");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(_folder, "example.tests", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    private static Dictionary<string, Secret> Secrets() => new(StringComparer.Ordinal) { ["client_secret"] = new(RecordedExample.ClientSecret) };

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
