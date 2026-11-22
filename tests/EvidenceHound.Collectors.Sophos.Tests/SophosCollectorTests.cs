using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.Sophos.Tests;

public sealed class SophosCollectorTests : IDisposable
{
    private static readonly string[] CheckIds = ["network.firewall.firmware_supported", "network.management.super_admin_count"];

    private static readonly string[] EndpointSettings = ["healthy_pass_percent", "healthy_partial_percent", "stale_agent_days"];

    private readonly RecordedSophos _sophos = RecordedSophos.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reports_every_check_from_the_recorded_tenant()
    {
        var result = (await CollectAsync()).Value();

        result.Checks.Select(c => c.CheckId.Value).ShouldBe(CheckIds);
        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass && c.Source == "example-sophos" && c.CollectedAt == Fixtures.Now);
        result.Checks[0].EvidenceRefs.ShouldBe(["firewalls.json"]);
        result.Checks[1].EvidenceRefs.ShouldBe(["admins.json", "roles.json"]);
        result.ControlStatuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reads_firewalls_admins_and_roles_only()
    {
        var result = (await CollectAsync()).Value();

        result.Files.Select(f => f.FileName).ShouldBe(["firewalls.json", "admins.json", "roles.json"]);
        var firewalls = JsonNode.Parse((await result.Files[0].ReadAsync(Token)).Content.Span)!;
        firewalls["request"]!.GetValue<string>().ShouldBe("firewall/v1/firewalls");
        firewalls["value"]!.AsArray().Count.ShouldBe(1);
        _sophos.Region.LogEntries.ShouldNotContain(e => e.RequestMessage!.Path.StartsWith("/endpoint/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_names_people_addresses_and_serial_numbers_are_hashed_in_evidence()
    {
        var result = (await CollectAsync()).Value();

        var text = string.Concat(await Task.WhenAll(result.Files.Select(async f => Encoding.UTF8.GetString((await f.ReadAsync(Token)).Content.Span))));
        text.ShouldNotContain("X0000EXAMPLE01");
        text.ShouldNotContain("198.51.100.1");
        text.ShouldNotContain("fw.example.com");
        text.ShouldContain("SFOS 21.0.1 MR-1-Build237");
        text.ShouldNotContain("Alex");
        text.ShouldNotContain("alex.admin@example.com");
        text.ShouldNotContain("Example Pty Ltd");
        text.ShouldContain("Super Admin");
    }

    [Fact]
    public async Task Admins_are_read_across_every_page()
    {
        var result = (await CollectAsync()).Value();

        var admins = JsonNode.Parse((await result.Files.Single(f => f.FileName == "admins.json").ReadAsync(Token)).Content.Span)!;
        admins["request"]!.GetValue<string>().ShouldBe("common/v1/admins");
        admins["pages"]!.GetValue<int>().ShouldBe(2);
        admins["value"]!.AsArray().Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("/common/v1/admins", "admins")]
    [InlineData("/common/v1/roles", "roles")]
    public async Task A_role_that_cannot_read_admins_or_roles_fails_with_forbidden(string path, string list)
    {
        _sophos.FailRegion(path, 403, "forbidden", "Forbidden");

        var problem = (await CollectAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.forbidden");
        problem.Message.ShouldContain(list);
    }

    [Fact]
    public async Task Identities_are_kept_when_configured()
    {
        var settings = _sophos.Settings();
        settings["include_identities"] = "true";

        var result = (await CollectAsync(settings)).Value();

        Encoding.UTF8.GetString((await result.Files[0].ReadAsync(Token)).Content.Span).ShouldContain("fw.example.com");
    }

    [Fact]
    public async Task Refused_credential_fails_the_run()
    {
        _sophos.RefuseToken();

        (await CollectAsync()).Problems().ShouldHaveSingleItem().Code.ShouldBe("sophos.auth_failed");
    }

    [Fact]
    public async Task Invalid_settings_fail_before_any_request()
    {
        var settings = _sophos.Settings();
        settings["supported_firewall_firmware"] = "latest";

        (await CollectAsync(settings)).Problems().ShouldHaveSingleItem().Code.ShouldBe("sophos.setting_invalid");
        _sophos.Id.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_secret_is_a_problem()
    {
        var context = Context(_sophos.Settings(), new Dictionary<string, Secret>());

        (await new SophosCollector().CollectAsync(context, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("sophos.secret_missing");
    }

    [Fact]
    public void Adding_the_super_admin_check_is_a_new_minor_version()
    {
        new SophosCollector().Version.ShouldBe("2.1.0");
    }

    [Fact]
    public void Describes_its_checks_settings_and_secrets()
    {
        var description = new SophosCollector().Describe();

        description.Checks.ShouldBe(CheckIds, ignoreOrder: true);
        description.Settings.Where(s => s.Required).Select(s => s.Name).ShouldBe(["client_id", "supported_firewall_firmware"], ignoreOrder: true);
        description.Settings.Select(s => s.Name).ShouldNotContain(name => EndpointSettings.Contains(name));
        description.Secrets.ShouldHaveSingleItem().Name.ShouldBe("client_secret");
        description.Secrets[0].Required.ShouldBeTrue();
    }

    public void Dispose() => _sophos.Dispose();

    private Task<Result<CollectionResult>> CollectAsync(Dictionary<string, string>? settings = null) =>
        new SophosCollector().CollectAsync(
            Context(settings ?? _sophos.Settings(), new Dictionary<string, Secret> { ["client_secret"] = new(RecordedSophos.ClientSecret) }), Token);

    private CollectorContext Context(Dictionary<string, string> settings, Dictionary<string, Secret> secrets) =>
        new("example-sophos", new CollectorSettings(settings), secrets, new FixedClock(Fixtures.Now), NullLogger.Instance, _sophos.HttpClients);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
