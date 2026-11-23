using System.Text;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime.Tests;

public sealed class CollectStepTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("hound-collect-").FullName;
    private readonly FixedClock _clock = new(Now);

    private static CollectorInstance Instance(params (string Key, string Reference)[] secrets) => new(
        "idp",
        "stub",
        "0 2 * * *",
        new Dictionary<string, string> { ["tenant"] = "example.com" },
        secrets.ToDictionary(s => s.Key, s => SecretReference.Parse(s.Reference).Value()));

    private static Result<CollectionResult> Output(CollectorContext context) => Result.Ok(new CollectionResult(
        [CheckResult.Create(CheckId.Create("identity.mfa.admins").Value(), CheckOutcome.Pass, "ok", ["admins.json"], context.Clock.UtcNow, context.Name).Value()],
        [CollectedEvidence.FromBytes("admins.json", "application/json", Encoding.UTF8.GetBytes("{}"))],
        []));

    private CollectStep Step(int? retentionDays = null, Dictionary<string, string>? environment = null) =>
        new(Secrets.From(environment ?? []), new FileEvidenceStore(_root), _clock, retentionDays, TestCollectorServices.None);

    [Fact]
    public async Task Stores_the_collector_output_with_a_manifest()
    {
        var collector = new StubCollector(Output);

        var run = (await Step().RunAsync(Instance(), collector, TestContext.Current.CancellationToken)).Value();

        run.Stored.Complete.ShouldBeTrue();
        run.Manifest.Collector.ShouldBe("idp");
        run.Manifest.CollectorVersion.ShouldBe("9.9.9");
        run.Manifest.CollectedAt.ShouldBe(Now);
        File.Exists(Path.Combine(run.Stored.Location, "admins.json")).ShouldBeTrue();
        File.Exists(Path.Combine(run.Stored.Location, Manifests.FileName)).ShouldBeTrue();
        var context = collector.Calls.ShouldHaveSingleItem();
        context.Name.ShouldBe("idp");
        context.Settings.Find("tenant").ShouldBe("example.com");
        context.Clock.UtcNow.ShouldBe(Now);
    }

    [Fact]
    public async Task The_capabilities_another_product_provides_are_passed_to_the_collector()
    {
        var collector = new StubCollector(Output);
        var at = new SourceLine("scope/capabilities.yaml", 1);
        var framework = new ScopeRef("urn:example:framework", at);
        var scope = new ScopeDocument(new ScopeSetup("Example Pty Ltd", [], []), [], [])
        {
            Capabilities = new ScopeCapabilityMap(framework, [new ScopeCapability("ac", CapabilityProvision.System, at) { Route = "example-app-control", Systems = [new ScopeRef("Example app control", at)], ApprovedBy = "GRC lead" }], at),
        };

        await Step().RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        collector.Calls.Single().Capabilities.ProvidedElsewhere.ShouldBeEmpty();

        var step = new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), _clock, null, TestCollectorServices.None) { Scope = scope };
        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        collector.Calls[1].Capabilities.ProvidedElsewhere.ShouldBe(new Dictionary<string, string> { ["ac"] = "Example app control (no audit yet)" });
    }

    [Fact]
    public async Task Resolved_secrets_are_passed_to_the_collector()
    {
        var collector = new StubCollector(Output);

        await Step(environment: new() { ["IDP_TOKEN"] = "hunter2" }).RunAsync(Instance(("api_token", "env:IDP_TOKEN")), collector, TestContext.Current.CancellationToken);

        collector.Calls.ShouldHaveSingleItem().Secrets["api_token"].Reveal().ShouldBe("hunter2");
    }

    [Fact]
    public async Task A_missing_secret_fails_before_collecting()
    {
        var collector = new StubCollector(Output);

        var result = await Step().RunAsync(Instance(("api_token", "env:IDP_TOKEN")), collector, TestContext.Current.CancellationToken);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("secret.missing");
        collector.Calls.ShouldBeEmpty();
        Directory.Exists(Path.Combine(_root, "idp")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_collection_stores_nothing()
    {
        var collector = new StubCollector(_ => Result.Fail<CollectionResult>(new Problem("collector.unavailable", "the system did not answer")));

        var result = await Step().RunAsync(Instance(), collector, TestContext.Current.CancellationToken);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("collector.unavailable");
        Directory.Exists(Path.Combine(_root, "idp")).ShouldBeFalse();
    }

    [Fact]
    public async Task Applies_retention_after_storing()
    {
        var collector = new StubCollector(Output);
        var step = Step(retentionDays: 30);
        _clock.UtcNow = Now.AddDays(-40);
        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        _clock.UtcNow = Now.AddDays(-10);
        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        _clock.UtcNow = Now;

        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);

        var runs = await new FileEvidenceStore(_root).ListRunsAsync("idp", TestContext.Current.CancellationToken);
        runs.Select(r => r.StartedAt).ShouldBe([Now.AddDays(-10), Now]);
    }

    [Fact]
    public async Task Keeps_the_final_run_of_each_closed_audit_period()
    {
        var collector = new StubCollector(Output);
        var step = new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), _clock, 30, TestCollectorServices.None)
        {
            PeriodEnds = [new DateOnly(2026, 3, 31)],
            PeriodRetentionYears = 7,
        };
        _clock.UtcNow = new DateTimeOffset(2026, 3, 30, 2, 0, 0, TimeSpan.Zero);
        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        _clock.UtcNow = new DateTimeOffset(2026, 3, 31, 2, 0, 0, TimeSpan.Zero);
        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);
        _clock.UtcNow = Now;

        await step.RunAsync(Instance(), collector, TestContext.Current.CancellationToken);

        var runs = await new FileEvidenceStore(_root).ListRunsAsync("idp", TestContext.Current.CancellationToken);
        runs.Select(r => r.StartedAt).ShouldBe([new DateTimeOffset(2026, 3, 31, 2, 0, 0, TimeSpan.Zero), Now]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
