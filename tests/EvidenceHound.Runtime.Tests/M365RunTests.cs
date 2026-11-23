using System.Text.Json.Nodes;
using EvidenceHound.Collectors.M365;
using EvidenceHound.Collectors.M365.Tests;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Runtime.Tests;

/// <summary>A full m365 collector run against the recorded example.com tenant, through the runner's collect step and evidence store.</summary>
public sealed class M365RunTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("hound-m365-run-").FullName;

    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    [Fact]
    public async Task Full_run_reports_every_check_and_writes_the_evidence_manifest()
    {
        var instance = new CollectorInstance("example-m365", M365Collector.TypeName, "0 3 * * *", _tenant.Settings(), new Dictionary<string, Core.Secrets.SecretReference>());
        var step = new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), new FixedClock(Now), null, new CollectorServices(NullLoggerFactory.Instance, _tenant.HttpClients));
        var collector = new M365Collector(new TestCredentialFactory(), new SystemGraphWait());

        var run = (await step.RunAsync(instance, collector, TestContext.Current.CancellationToken)).Value();

        run.Output.Checks.Select(c => c.CheckId.Value).ShouldBe(collector.Describe().Checks, ignoreOrder: true);
        run.Output.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
        run.Stored.Complete.ShouldBeTrue();
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.Stored.Location, Manifests.FileName), TestContext.Current.CancellationToken))!;
        manifest.ToJsonString().ShouldContain("configuration-policies.json");
        run.Manifest.Files.Count.ShouldBe(12);
        run.Manifest.Files.ShouldAllBe(f => File.Exists(Path.Combine(run.Stored.Location, f.FileName)));
    }

    public void Dispose()
    {
        _tenant.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
