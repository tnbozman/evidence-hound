using EvidenceHound.Collectors.Testing;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class RestCollectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory("hound-profiles-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Invalid_profile_reports_its_checks_unable_to_assess()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "example.yaml"), Profiles.ExampleYaml.Replace("over: user_roles", "over: people", StringComparison.Ordinal), Token);

        var result = (await Collect(Settings())).Value();

        result.Checks.Select(c => c.CheckId.Value).ShouldBe(["example.device.os_supported", "example.device.recently_seen", "example.identity.mfa_users", "example.identity.owner_count"]);
        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error && c.Source == "example-rest");
        result.Checks[0].Observation.ShouldStartWith("Unable to assess: profile 'example' is not valid");
        result.Checks[0].Observation.ShouldContain("profile.reference_unknown");
    }

    [Fact]
    public async Task Missing_required_parameter_reports_checks_unable_to_assess()
    {
        await CopyExample();
        var settings = Settings();
        settings.Remove("supported_os");

        var result = (await Collect(settings)).Value();

        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error && c.Observation.Contains("profile.parameter_missing"));
    }

    [Fact]
    public async Task Missing_profile_fails_naming_the_folder()
    {
        var problem = (await Collect(Settings())).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("profile.not_found");
        problem.Message.ShouldContain(_folder);
    }

    [Theory]
    [InlineData("../example")]
    [InlineData("sub/example")]
    [InlineData("Example")]
    public async Task Profile_name_must_be_a_plain_name(string name)
    {
        await CopyExample();
        var settings = Settings();
        settings["profile"] = name;

        (await Collect(settings)).Problems().ShouldHaveSingleItem().Code.ShouldBe("profile.name_invalid");
    }

    [Fact]
    public async Task Describes_the_checks_of_the_instance_profile()
    {
        await CopyExample();

        var checks = new RestCollector().Describe().ChecksFrom!(new CollectorSettings(Settings()));

        checks.ShouldBe(["example.device.os_supported", "example.device.recently_seen", "example.identity.mfa_users", "example.identity.owner_count"]);
    }

    [Fact]
    public async Task Validates_an_instance_against_its_profile()
    {
        await CopyExample();
        var settings = Settings();
        settings["admin_max"] = "two";

        var problems = new RestCollector().ValidateInstance(new CollectorSettings(settings), ["client_secret"]);

        problems.ShouldHaveSingleItem().Code.ShouldBe("profile.parameter_invalid");
    }

    private Task CopyExample() => File.WriteAllTextAsync(Path.Combine(_folder, "example.yaml"), Profiles.ExampleYaml, Token);

    private Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        ["profile"] = "example",
        ["profiles_path"] = _folder,
        ["client_id"] = "00000000-0000-4000-8000-0000000000c1",
        ["supported_os"] = "14, 15",
    };

    private static Task<Core.Results.Result<CollectionResult>> Collect(Dictionary<string, string> settings) =>
        new RestCollector().CollectAsync(
            new CollectorContext(
                "example-rest",
                new CollectorSettings(settings),
                new Dictionary<string, Secret>(StringComparer.Ordinal) { ["client_secret"] = new("example-client-secret-0d9e") },
                new FixedClock(Now),
                NullLogger.Instance,
                OfflineHttpClients.Instance),
            Token);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
