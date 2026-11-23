using System.Text.Json.Nodes;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.M365DevServer;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>The representative Example Pty Ltd tenant (s09-44): served offline, consistent, and right about every check.</summary>
public sealed class M365DevTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_collection_the_collector_reads_is_served()
    {
        using var server = DevGraphServer.Start(DevTenant.Compliant);

        var result = (await CollectAsync(server)).Value();

        result.Files.Select(f => f.FileName).ShouldBe(GraphCollections.Paths.Keys.Select(name => $"{name}.json"), ignoreOrder: true);
        server.Requests.ShouldAllBe(r => r.Status == 200);
        server.Requests.Count(r => r.Path == GraphCollections.Paths["users"]).ShouldBeGreaterThan(1, "users come in more than one page");
    }

    [Fact]
    public void Ids_are_the_same_across_collections()
    {
        var items = GraphCollections.Render(DevTenant.Compliant, new Uri("https://graph.example.test/"));
        var users = items["users"].Select(u => Text(u, "id")).ToHashSet(StringComparer.Ordinal);
        var upns = items["users"].Select(u => Text(u, "userPrincipalName")).ToHashSet(StringComparer.Ordinal);

        items["mfa-registration"].Select(r => Text(r, "id")).ShouldBe(users, ignoreOrder: true);
        foreach (var name in new[] { "role-assignments", "role-eligibility-schedules", "role-assignment-schedule-instances" })
        {
            items[name].Select(g => Text(g, "principalId")).ShouldAllBe(id => users.Contains(id), name);
        }

        items["managed-devices"].Select(d => Text(d, "userPrincipalName")).ShouldAllBe(upn => upns.Contains(upn));
        items["conditional-access-policies"].SelectMany(p => p!["conditions"]!["users"]!["excludeUsers"]!.AsArray().Select(u => u!.GetValue<string>()))
            .ShouldAllBe(id => users.Contains(id));
        DevTenant.Settings()[M365Settings.BreakGlassAccounts].Split(", ").ShouldAllBe(id => users.Contains(id));
        upns.ShouldAllBe(upn => upn.EndsWith("@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Compliant_tenant_passes_every_check()
    {
        using var server = DevGraphServer.Start(DevTenant.Compliant);

        var result = (await CollectAsync(server)).Value();

        result.Checks.Count.ShouldBe(13);
        result.Checks.Where(c => c.Outcome != CheckOutcome.Pass).Select(c => $"{c.CheckId.Value}: {c.Observation}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_deviation_fails_its_named_check()
    {
        DevTenant.Deviations.Count.ShouldBeGreaterThanOrEqualTo(8);
        foreach (var deviation in DevTenant.Deviations)
        {
            using var server = DevGraphServer.Start(DevTenant.Compliant.With(deviation));

            var checks = (await CollectAsync(server)).Value().Checks.ToDictionary(c => c.CheckId.Value, c => c.Outcome, StringComparer.Ordinal);

            foreach (var (check, outcome) in checks)
            {
                var expected = deviation.Fails.TryGetValue(check, out var named) ? named : CheckOutcome.Pass;
                outcome.ShouldBe(expected, $"{deviation.Name}: {check}");
            }
        }
    }

    [Fact]
    public async Task The_gaps_tenant_carries_every_deviation()
    {
        using var server = DevGraphServer.Start(DevTenant.For(DevVariant.Gaps));

        var checks = (await CollectAsync(server)).Value().Checks;

        var named = DevTenant.Deviations.SelectMany(d => d.Fails.Keys).ToHashSet(StringComparer.Ordinal);
        checks.Where(c => named.Contains(c.CheckId.Value)).ShouldAllBe(c => c.Outcome != CheckOutcome.Pass);
        checks.Where(c => !named.Contains(c.CheckId.Value)).ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
    }

    [Fact]
    public void Blueprint_settings_keep_their_source_and_attribution()
    {
        Blueprint.Notice.ShouldContain("© Commonwealth of Australia 2023");
        Blueprint.Notice.ShouldContain("Creative Commons Attribution 4.0");
        Blueprint.Notice.ShouldContain(Blueprint.Commit);
        Blueprint.Commit.Length.ShouldBe(40);
        Blueprint.SourceUrl.ShouldStartWith("https://github.com/ASD-Blueprint/ASD-Blueprint-for-Secure-Cloud/");

        var items = GraphCollections.Render(DevTenant.Compliant, new Uri("https://graph.example.test/"));
        var office = items["configuration-policies"].Single(p => Text(p, "name") == "ASD Office Hardening Guidelines")!;
        office["settings"]!.ToJsonString().ShouldBe(Blueprint.OfficeHardening()["settings"]!.ToJsonString());
        items["configuration-policies"].Concat(items["conditional-access-policies"])
            .ShouldAllBe(p => Text(p, "description").Contains("ASD Blueprint for Secure Cloud", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_variant_lists_its_deviations_in_the_tutorial()
    {
        var tutorial = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "m365-dev-server.md"));

        foreach (var deviation in DevTenant.Deviations)
        {
            tutorial.ShouldContain($"`{deviation.Name}`");
        }

        tutorial.ShouldContain("make m365-dev");
        tutorial.ShouldContain("© Commonwealth of Australia 2023");
    }

    private static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>() ?? string.Empty;

    private static Task<Result<CollectionResult>> CollectAsync(DevGraphServer server)
    {
        var settings = DevTenant.Settings();
        settings[M365Settings.GraphUrl] = server.Url.ToString();
        var context = new CollectorContext("example-m365", new CollectorSettings(settings), new Dictionary<string, Secret>(), new FixedClock(Now), NullLogger.Instance, server.HttpClients);
        return new M365Collector(new Credentials(), new RecordingWait()).CollectAsync(context, Token);
    }

    private sealed class Credentials : IGraphCredentialFactory
    {
        public Result<Azure.Core.TokenCredential> Create(GraphConnection connection) => Result.Ok(TestCredentials.Working);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
