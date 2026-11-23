using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.SophosDevServer.Tests;

/// <summary>The fictional Example Pty Ltd Sophos Central tenant (s09-52), read by the sophos-central profile.</summary>
public sealed class SophosDevTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Profiles => Path.Combine(AppContext.BaseDirectory, "profiles");

    [Fact]
    public async Task Every_query_the_profile_reads_is_served()
    {
        using var server = DevSophosServer.Start(SophosDevTenant.Compliant);

        var result = (await CollectAsync(server)).Value();

        result.Files.Select(f => f.FileName).ShouldBe(["firewalls.json", "admins.json", "roles.json"], ignoreOrder: true);
        server.Requests.ShouldAllBe(r => r.Status == 200);
        server.Requests.Count(r => r.Path == "/common/v1/admins").ShouldBeGreaterThan(1, "admins come in more than one page");
    }

    [Fact]
    public void Ids_are_the_same_across_answers()
    {
        var tenant = SophosDevTenant.Compliant;
        var url = new Uri("http://sophos.example.test/");
        var whoami = SophosAnswers.Whoami(tenant, url);
        var items = SophosAnswers.Items(tenant);
        var roles = items["roles"].Select(r => Text(r, "id")).ToHashSet(StringComparer.Ordinal);

        Text(whoami, "id").ShouldBe(tenant.TenantId);
        whoami["apiHosts"]!["dataRegion"]!.GetValue<string>().ShouldBe("http://sophos.example.test");
        items["firewalls"].Select(f => f!["tenant"]!["id"]!.GetValue<string>()).ShouldAllBe(id => id == tenant.TenantId);
        items["admins"].SelectMany(a => a!["roleAssignments"]!.AsArray().Select(r => Text(r, "roleId"))).ShouldAllBe(id => roles.Contains(id));
        items["admins"].Select(a => a!["profile"]!["email"]!.GetValue<string>()).ShouldAllBe(e => e.EndsWith("@example.com", StringComparison.Ordinal));
        items["firewalls"].Select(f => Text(f, "hostname")).ShouldAllBe(h => h.EndsWith(".example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Compliant_tenant_passes_every_check()
    {
        using var server = DevSophosServer.Start(SophosDevTenant.Compliant);

        var checks = (await CollectAsync(server)).Value().Checks;

        checks.Select(c => c.CheckId.Value).ShouldBe(["network.firewall.firmware_supported", "network.management.super_admin_count"]);
        checks.Where(c => c.Outcome != CheckOutcome.Pass).Select(c => $"{c.CheckId.Value}: {c.Observation}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_deviation_fails_its_named_check()
    {
        SophosDevTenant.Deviations.Count.ShouldBeGreaterThanOrEqualTo(2);
        foreach (var deviation in SophosDevTenant.Deviations)
        {
            using var server = DevSophosServer.Start(SophosDevTenant.Compliant.With(deviation));

            var checks = (await CollectAsync(server)).Value().Checks;

            foreach (var check in checks)
            {
                var expected = deviation.Fails.TryGetValue(check.CheckId.Value, out var named) ? named : CheckOutcome.Pass;
                check.Outcome.ShouldBe(expected, $"{deviation.Name}: {check.CheckId.Value}: {check.Observation}");
            }
        }

        using var gaps = DevSophosServer.Start(SophosDevTenant.For(DevVariant.Gaps));
        (await CollectAsync(gaps)).Value().Checks.ShouldAllBe(c => c.Outcome != CheckOutcome.Pass);
    }

    [Fact]
    public async Task The_token_address_checks_no_credential()
    {
        using var server = DevSophosServer.Start(SophosDevTenant.Compliant);
        using var http = new HttpClient();

        using var none = await http.PostAsync(new Uri(server.Url, "api/v2/oauth2/token"), new StringContent(string.Empty), Token);
        using var any = await http.PostAsync(new Uri(server.Url, "api/v2/oauth2/token"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "anything",
            ["client_secret"] = "anything-else",
        }), Token);

        foreach (var response in new[] { none, any })
        {
            response.IsSuccessStatusCode.ShouldBeTrue();
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(Token);
            Text(body, "access_token").ShouldBe(SophosDevTenant.AccessToken);
        }
    }

    [Fact]
    public void Tutorial_lists_every_deviation()
    {
        var tutorial = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "sophos-dev-server.md"));

        foreach (var deviation in SophosDevTenant.Deviations)
        {
            tutorial.ShouldContain($"`{deviation.Name}`");
        }

        tutorial.ShouldContain("make sophos-dev");
        tutorial.ShouldContain("make trial");
    }

    private static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>() ?? string.Empty;

    private static Task<Result<CollectionResult>> CollectAsync(DevSophosServer server)
    {
        var settings = SophosDevTenant.Settings(server.Url);
        settings["profiles_path"] = Profiles;
        var secrets = new Dictionary<string, Secret>(StringComparer.Ordinal) { ["client_secret"] = new(SophosDevTenant.ClientSecret) };
        var context = new CollectorContext("sophos", new CollectorSettings(settings), secrets, new FixedClock(Now), NullLogger.Instance, server.HttpClients);
        return new RestCollector().CollectAsync(context, Token);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
