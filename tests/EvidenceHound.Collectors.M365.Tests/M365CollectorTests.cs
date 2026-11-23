using System.Text.Json.Nodes;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.M365.Tests;

public sealed class M365CollectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] CheckIds =
    [
        "identity.mfa.registered_all_users", "identity.mfa.enforced_conditional_access", "identity.legacy_auth_blocked", "identity.mfa.phishing_resistant_admins",
        "identity.privileged.global_admin_count", "identity.privileged.admins_cloud_only", "identity.privileged.pim_eligible_not_permanent",
        "endpoint.managed.compliant_ratio", "endpoint.os.supported_versions", "apps.office.macros_blocked_from_internet", "endpoint.asr.office_rules_enabled",
        "endpoint.application_control.enforced", "endpoint.application_control.file_types_covered", "endpoint.application_control.events_centrally_logged",
    ];

    [Fact]
    public async Task Reports_every_check_from_the_recorded_tenant()
    {
        var result = (await CollectAsync(_tenant)).Value();

        result.Checks.Select(c => c.CheckId.Value).ShouldBe(CheckIds);
        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass && c.Source == "example-m365" && c.CollectedAt == Now);
        result.Checks[0].EvidenceRefs.ShouldBe(["users.json", "mfa-registration.json"]);
        result.ControlStatuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Users_from_three_pages_are_one_evidence_file()
    {
        var result = (await CollectAsync(_tenant)).Value();

        var users = await result.Files.Single(f => f.FileName == "users.json").ReadAsync(Token);
        var content = JsonNode.Parse(users.Content.Span)!;
        content["pages"]!.GetValue<int>().ShouldBe(3);
        content["value"]!.AsArray().Count.ShouldBe(9);
        content.ToJsonString().ShouldNotContain("@example.com");
        _tenant.Server.LogEntries.Count(e => e.RequestMessage!.Path == "/v1.0/users").ShouldBe(3);
    }

    [Fact]
    public async Task Device_names_and_their_users_are_hashed_in_evidence()
    {
        var result = (await CollectAsync(_tenant)).Value();

        var devices = await result.Files.Single(f => f.FileName == "managed-devices.json").ReadAsync(Token);
        var text = System.Text.Encoding.UTF8.GetString(devices.Content.Span);
        text.ShouldNotContain("EX-LT-0001");
        text.ShouldNotContain("@example.com");
        text.ShouldContain("10.0.26100.2314");
    }

    [Fact]
    public async Task Writes_one_evidence_file_per_collection()
    {
        var result = (await CollectAsync(_tenant)).Value();

        result.Files.Select(f => f.FileName).ShouldBe(
            ["users.json", "mfa-registration.json", "conditional-access-policies.json", "role-assignments.json", "role-eligibility-schedules.json", "role-assignment-schedule-instances.json",
             "managed-devices.json", "device-configurations.json", "configuration-policies.json",
             "hunting-app-control-events.json", "hunting-app-control-blocked-files.json", "hunting-defender-onboarding.json"]);
    }

    [Fact]
    public async Task Without_pim_the_pim_check_is_not_applicable_and_the_run_succeeds()
    {
        _tenant.WithoutPim();

        var result = (await CollectAsync(_tenant)).Value();

        var pim = result.Checks.Single(c => c.CheckId.Value == "identity.privileged.pim_eligible_not_permanent");
        pim.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        pim.EvidenceRefs.ShouldBe(["role-assignments.json"]);
        result.Files.Select(f => f.FileName).ShouldNotContain("role-eligibility-schedules.json");
        result.Files.Select(f => f.FileName).ShouldNotContain("role-assignment-schedule-instances.json");
    }

    [Fact]
    public async Task Without_advanced_hunting_the_run_succeeds_and_the_events_check_says_why()
    {
        foreach (var name in new[] { "hunting-app-control-events", "hunting-app-control-blocked-files", "hunting-defender-onboarding" })
        {
            _tenant.Fail(name, 403, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.");
        }

        var result = (await CollectAsync(_tenant)).Value();

        var events = result.Checks.Single(c => c.CheckId.Value == "endpoint.application_control.events_centrally_logged");
        events.Outcome.ShouldBe(CheckOutcome.Error);
        events.Observation.ShouldContain("ThreatHunting.Read.All");
        result.Files.Select(f => f.FileName).ShouldNotContain("hunting-app-control-events.json");
        result.Checks.Single(c => c.CheckId.Value == "endpoint.application_control.enforced").Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public async Task A_missing_licence_for_a_required_collection_still_fails_the_run()
    {
        _tenant.Fail("users", 400, "AadPremiumLicenseRequired", "A license is required.");

        (await CollectAsync(_tenant)).Problems().ShouldHaveSingleItem().Code.ShouldBe(GraphClient.NotLicensed);
    }

    [Fact]
    public async Task Identities_are_kept_when_configured()
    {
        var settings = _tenant.Settings();
        settings["include_identities"] = "true";

        var result = (await CollectAsync(_tenant, settings)).Value();

        var users = await result.Files.Single(f => f.FileName == "users.json").ReadAsync(Token);
        System.Text.Encoding.UTF8.GetString(users.Content.Span).ShouldContain("alex.admin@example.com");
    }

    [Fact]
    public async Task Missing_permission_fails_the_run_naming_it()
    {
        _tenant.Fail("mfa-registration", 403, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.");

        var problem = (await CollectAsync(_tenant)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.graph_forbidden");
        problem.Message.ShouldContain("AuditLog.Read.All");
    }

    [Fact]
    public async Task Invalid_settings_fail_before_any_request()
    {
        var settings = _tenant.Settings();
        settings["mfa_partial_percent"] = "lots";

        (await CollectAsync(_tenant, settings)).Problems().ShouldHaveSingleItem().Code.ShouldBe("m365.setting_invalid");
        _tenant.Server.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Credentials_come_from_the_secrets()
    {
        var factory = new CapturingFactory();
        var context = Context(_tenant.Settings(), new Dictionary<string, Secret> { ["client_secret"] = new("example-client-secret-value-1b8e") });

        await new M365Collector(factory, new RecordingWait()).CollectAsync(context, Token);

        var connection = factory.Connections.ShouldHaveSingleItem();
        (connection.TenantId, connection.ClientId).ShouldBe((RecordedTenant.TenantId, RecordedTenant.ClientId));
        connection.ClientSecret!.Reveal().ShouldBe("example-client-secret-value-1b8e");
        connection.ClientCertificate.ShouldBeNull();
    }

    [Fact]
    public void Describes_its_checks_settings_and_secrets()
    {
        var description = new M365Collector(new CapturingFactory(), new RecordingWait()).Describe();

        description.Checks.ShouldBe(CheckIds, ignoreOrder: true);
        description.Settings.Where(s => s.Required).Select(s => s.Name).ShouldBe(["tenant_id", "client_id", "supported_windows_builds"], ignoreOrder: true);
        description.Secrets.Select(s => s.Name).ShouldBe(["client_certificate", "client_secret"]);
        description.Secrets.ShouldAllBe(s => !s.Required);
    }

    public void Dispose() => _tenant.Dispose();

    private static Task<Result<CollectionResult>> CollectAsync(RecordedTenant tenant, Dictionary<string, string>? settings = null) =>
        new M365Collector(new CapturingFactory(), new RecordingWait()).CollectAsync(Context(settings ?? tenant.Settings(), new Dictionary<string, Secret>(), tenant.HttpClients), Token);

    private CollectorContext Context(Dictionary<string, string> settings, Dictionary<string, Secret> secrets) => Context(settings, secrets, _tenant.HttpClients);

    private static CollectorContext Context(Dictionary<string, string> settings, Dictionary<string, Secret> secrets, IHttpClientFactory http) =>
        new("example-m365", new CollectorSettings(settings), secrets, new FixedClock(Now), NullLogger.Instance, http);

    private sealed class CapturingFactory : IGraphCredentialFactory
    {
        public List<GraphConnection> Connections { get; } = [];

        public Result<Azure.Core.TokenCredential> Create(GraphConnection connection)
        {
            Connections.Add(connection);
            return Result.Ok(TestCredentials.Working);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
