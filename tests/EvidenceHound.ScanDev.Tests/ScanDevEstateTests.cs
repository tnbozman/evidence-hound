using EvidenceHound.Collectors;
using EvidenceHound.Collectors.VulnScan;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.ScanDev.Tests;

/// <summary>The fictional Example Pty Ltd scan exports (s09-58), read by the vuln-scan collector, one instance per scope.</summary>
public sealed class ScanDevEstateTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 7, 0, 0, TimeSpan.Zero);

    private readonly string _out = Directory.CreateTempSubdirectory("hound-scan-dev-tests-").FullName;

    [Fact]
    public async Task Compliant_estate_passes_all_eleven_checks()
    {
        ScanDevWriter.Write(ScanDevEstate.Compliant, _out, Today);

        var checks = await CollectAllAsync();

        checks.Select(c => c.CheckId.Value).ShouldBe(PatchChecks.All, ignoreOrder: true);
        checks.Where(c => c.Outcome != CheckOutcome.Pass).Select(c => $"{c.CheckId.Value}: {c.Observation}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_deviation_fails_only_its_named_checks()
    {
        ScanDevEstate.Deviations.Count.ShouldBeGreaterThanOrEqualTo(5);
        foreach (var deviation in ScanDevEstate.Deviations)
        {
            ScanDevWriter.Write(ScanDevEstate.Compliant.With(deviation), _out, Today);

            foreach (var check in await CollectAllAsync())
            {
                var expected = deviation.Fails.TryGetValue(check.CheckId.Value, out var named) ? named : CheckOutcome.Pass;
                check.Outcome.ShouldBe(expected, $"{deviation.Name}: {check.CheckId.Value}: {check.Observation}");
            }
        }
    }

    [Fact]
    public async Task Gaps_estate_fails_every_named_check()
    {
        ScanDevWriter.Write(ScanDevEstate.For("gaps"), _out, Today);

        var failing = (await CollectAllAsync()).Where(c => c.Outcome != CheckOutcome.Pass).Select(c => c.CheckId.Value).ToArray();

        failing.ShouldBe(ScanDevEstate.Deviations.SelectMany(d => d.Fails.Keys).Distinct(StringComparer.Ordinal), ignoreOrder: true);
    }

    [Fact]
    public async Task Writing_again_replaces_the_exports_so_the_latest_estate_is_current()
    {
        ScanDevWriter.Write(ScanDevEstate.Compliant, _out, Today);
        ScanDevWriter.Write(ScanDevEstate.For("gaps"), _out, Today);

        (await CollectAllAsync()).ShouldContain(c => c.Outcome == CheckOutcome.Fail);
        Directory.GetFiles(Path.Combine(_out, "internet-facing")).Length.ShouldBe(1);
    }

    [Fact]
    public void Every_scope_has_one_source_with_its_settings()
    {
        var sources = ScanDevEstate.Compliant.Sources("../scans");

        sources.Select(s => s.Settings[VulnScanSettings.Scope]).ShouldBe(VulnScanSource.Scopes, ignoreOrder: true);
        sources.Single(s => s.Settings[VulnScanSettings.Scope] == "internet_facing").Settings[VulnScanSettings.DropFolder].ShouldBe("../scans/internet-facing");
        sources.Single(s => s.Settings[VulnScanSettings.Scope] == "common_apps").Settings[VulnScanSettings.Format].ShouldBe("csv");
        sources.Select(s => s.Name).ShouldAllBe(n => n.EndsWith("-scans", StringComparison.Ordinal));
    }

    [Fact]
    public void Exports_hold_only_fictional_hosts()
    {
        ScanDevWriter.Write(ScanDevEstate.For("gaps"), _out, Today);

        foreach (var file in Directory.EnumerateFiles(_out, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            text.ShouldContain(".example.com");
            text.ShouldNotContain("@");
        }
    }

    [Fact]
    public void Options_parse_variant_deviations_out_and_today()
    {
        var options = ScanDevOptions.Parse(["--deviation", "browser-patch-overdue", "--out", "/tmp/scans", "--today", "2026-11-20"]).ShouldBeOfType<ScanDevOptions>();

        options.Deviations.ShouldBe(["browser-patch-overdue"]);
        options.Out.ShouldBe("/tmp/scans");
        options.Today.ShouldBe(new DateOnly(2026, 11, 20));
        ScanDevOptions.Parse(["--variant", "partly"]).ShouldBe("--variant is compliant or gaps, not 'partly'.");
        ScanDevOptions.Parse(["--today", "yesterday"]).ShouldBe("--today needs a date as yyyy-MM-dd, not 'yesterday'.");
        ScanDevOptions.Parse(["--deviation", "nope"]).ShouldBeOfType<string>().ShouldStartWith("--deviation is one of ");
        ScanDevOptions.Parse([]).ShouldBeOfType<ScanDevOptions>().Out.ShouldBe(".dev/scans");
    }

    [Fact]
    public void Tutorial_lists_every_deviation()
    {
        var tutorial = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "scan-dev.md"));

        foreach (var deviation in ScanDevEstate.Deviations)
        {
            tutorial.ShouldContain($"`{deviation.Name}`");
        }

        tutorial.ShouldContain("make scan-dev");
        tutorial.ShouldContain("make trial");
    }

    public void Dispose() => Directory.Delete(_out, recursive: true);

    private async Task<IReadOnlyList<CheckResult>> CollectAllAsync()
    {
        var checks = new List<CheckResult>();
        foreach (var source in ScanDevEstate.Compliant.Sources(_out))
        {
            var context = new CollectorContext(source.Name, new CollectorSettings(source.Settings), new Dictionary<string, Secret>(), new FixedClock(Now), NullLogger.Instance, new NoHttpClients());
            var result = await new VulnScanCollector().CollectAsync(context, TestContext.Current.CancellationToken);
            checks.AddRange(result.Match(r => r.Checks, problems => throw new ShouldAssertException(string.Join("; ", problems.Select(p => p.Message)))));
        }

        return checks;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No HTTP.");
    }
}
