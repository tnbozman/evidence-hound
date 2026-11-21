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

    [Fact]
    public async Task Reports_the_mfa_checks_from_the_recorded_tenant()
    {
        var result = (await CollectAsync(_tenant)).Value();

        result.Checks.Select(c => c.CheckId.Value).ShouldBe(
            ["identity.mfa.registered_all_users", "identity.mfa.enforced_conditional_access", "identity.legacy_auth_blocked", "identity.mfa.phishing_resistant_admins"]);
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
    public async Task Writes_one_evidence_file_per_collection()
    {
        var result = (await CollectAsync(_tenant)).Value();

        result.Files.Select(f => f.FileName).ShouldBe(["users.json", "mfa-registration.json", "conditional-access-policies.json"]);
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

        description.Checks.ShouldBe(
            ["identity.mfa.registered_all_users", "identity.mfa.enforced_conditional_access", "identity.legacy_auth_blocked", "identity.mfa.phishing_resistant_admins"],
            ignoreOrder: true);
        description.Settings.Where(s => s.Required).Select(s => s.Name).ShouldBe(["tenant_id", "client_id"], ignoreOrder: true);
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
