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

    [Fact]
    public async Task Saves_each_query_of_the_example_profile_as_one_evidence_file()
    {
        await CopyExample();
        using var api = RecordedExample.Start();

        var result = (await Collect(api.Settings(_folder), RecordedExample.HttpClients)).Value();

        result.Files.Select(f => f.FileName).Take(3).ShouldBe(["devices.json", "users.json", "roles.json"]);
        var devices = System.Text.Json.Nodes.JsonNode.Parse((await result.Files[0].ReadAsync(Token)).Content.Span)!;
        devices["pages"]!.GetValue<int>().ShouldBe(2);
        devices["item_count"]!.GetValue<int>().ShouldBe(3);
        devices["request"]!.GetValue<string>().ShouldEndWith("/v1/orgs/org-0001/devices");
        devices.ToJsonString().ShouldNotContain("laptop-01.example.com");
    }

    [Fact]
    public async Task Assesses_every_check_of_the_example_profile_and_saves_its_dataset()
    {
        await CopyExample();
        using var api = RecordedExample.Start();

        var result = (await Collect(api.Settings(_folder), RecordedExample.HttpClients)).Value();

        result.Checks.Select(c => (c.CheckId.Value, c.Outcome)).ShouldBe([
            ("example.device.os_supported", CheckOutcome.Pass),
            ("example.device.recently_seen", CheckOutcome.Pass),
            ("example.identity.mfa_users", CheckOutcome.Pass),
            ("example.identity.owner_count", CheckOutcome.Pass),
        ]);
        result.Checks[0].Observation.ShouldStartWith("3 of 3 devices run a supported operating system.");
        result.Checks[0].EvidenceRefs.ShouldBe(["devices.json"]);
        result.Checks[2].EvidenceRefs.ShouldBe(["dataset.user_roles.json"]);
        var dataset = System.Text.Json.Nodes.JsonNode.Parse((await result.Files.Single(f => f.FileName == "dataset.user_roles.json").ReadAsync(Token)).Content.Span)!;
        dataset["row_count"]!.GetValue<int>().ShouldBe(2);
        dataset["sources"]!.AsArray().Select(s => s!["file"]!.GetValue<string>()).ShouldBe(["users.json", "roles.json"]);
        dataset["sources"]![0]!["sha256"]!.GetValue<string>().Length.ShouldBe(64);
    }

    [Fact]
    public async Task Independent_checks_still_run_when_one_query_fails()
    {
        await CopyExample();
        using var api = RecordedExample.Start().Fail("/v1/orgs/org-0001/roles", 500);

        var result = (await Collect(api.Settings(_folder), RecordedExample.HttpClients)).Value();

        result.Checks.Select(c => c.Outcome).ShouldBe([CheckOutcome.Pass, CheckOutcome.Pass, CheckOutcome.Error, CheckOutcome.Error]);
        result.Checks[2].Observation.ShouldContain("'roles'");
        result.Files.Select(f => f.FileName).ShouldNotContain("roles.json");
    }

    private Task CopyExample() => File.WriteAllTextAsync(Path.Combine(_folder, "example.yaml"), Profiles.ExampleYaml, Token);

    private Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        ["profile"] = "example",
        ["profiles_path"] = _folder,
        ["client_id"] = "00000000-0000-4000-8000-0000000000c1",
        ["supported_os"] = "14, 15",
    };

    private static Task<Core.Results.Result<CollectionResult>> Collect(Dictionary<string, string> settings, IHttpClientFactory? clients = null) =>
        new RestCollector().CollectAsync(
            new CollectorContext(
                "example-rest",
                new CollectorSettings(settings),
                new Dictionary<string, Secret>(StringComparer.Ordinal) { ["client_secret"] = new(RecordedExample.ClientSecret) },
                new FixedClock(Now),
                NullLogger.Instance,
                clients ?? OfflineHttpClients.Instance),
            Token);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
