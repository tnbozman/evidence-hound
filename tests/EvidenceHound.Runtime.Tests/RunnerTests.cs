using System.Text;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Pipeline;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime.Tests;

public sealed class RunnerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("hound-runner-").FullName;
    private readonly RecordingSink _sink = new();

    private static CheckId Id(string id) => CheckId.Create(id).Value();

    private static readonly FrameworkMapping Mapping = FrameworkMapping.Create("urn:example:x", [
        RequirementMapping.Create("R-MFA", [Id("identity.mfa.admins")], MappingRule.AllPass).Value(),
        RequirementMapping.Create("R-BACKUP", [Id("backup.daily")], MappingRule.AllPass).Value(),
    ]).Value();

    private static readonly AuditTarget Target = new("EXAMPLE-1", new Dictionary<string, string> { ["mfa"] = "AC-MFA" });

    private static CollectorInstance Instance(string name, string type) =>
        new(name, type, "0 2 * * *", new Dictionary<string, string>(), new Dictionary<string, SecretReference>());

    private static StubCollector Reporting(string checkId, string file) => new(context => Result.Ok(new CollectorOutput(
        [CheckResult.Create(Id(checkId), CheckOutcome.Pass, "ok", [file], context.Now, context.Name).Value()],
        [new CollectedFile(file, "application/json", Encoding.UTF8.GetBytes("{\"ok\": true}"))],
        [])));

    private Runner Runner(Dictionary<string, ICollector> collectors) => new(
        collectors,
        new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), new FixedClock(Now), retentionDays: null),
        Mapping,
        Target,
        files => _sink.With(files));

    [Fact]
    public async Task Runs_each_configured_collector()
    {
        var runner = Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json"), ["backup"] = Reporting("backup.daily", "jobs.json") });

        var summary = await runner.RunAsync([Instance("example-idp", "idp"), Instance("example-backup", "backup")], new RunOptions(), TestContext.Current.CancellationToken);

        summary.Collectors.Select(c => (c.Collector, c.Outcome)).ShouldBe([("example-idp", CollectorOutcome.Succeeded), ("example-backup", CollectorOutcome.Succeeded)]);
        _sink.Batches.Count.ShouldBe(2);
        _sink.Batches[0].Select(u => u.GetType().Name).ShouldBe(["UploadEvidence", "UpdateRequirementAssessment"]);
        _sink.Batches[0].OfType<AuditUpdate.UploadEvidence>().Single().Evidence.Id.ShouldBe("example-idp/admins.json");
        var requirement = _sink.Batches[0].OfType<AuditUpdate.UpdateRequirementAssessment>().Single();
        (requirement.ComplianceAssessmentRefId, requirement.RequirementRefId, requirement.Result).ShouldBe(("EXAMPLE-1", "R-MFA", ComplianceResult.Compliant));
        requirement.EvidenceRefIds.ShouldBe(["example-idp/admins.json"]);
        _sink.Batches[1].OfType<AuditUpdate.UpdateRequirementAssessment>().Single().RequirementRefId.ShouldBe("R-BACKUP");
        summary.Collectors[0].Publish!.Sent.ShouldBe(2);
    }

    [Fact]
    public async Task Failure_in_one_collector_continues_others()
    {
        var failing = new StubCollector(_ => Result.Fail<CollectorOutput>(new Problem("collector.unavailable", "the system did not answer")));
        var runner = Runner(new()
        {
            ["failing"] = failing,
            ["throwing"] = new ThrowingCollector(),
            ["idp"] = Reporting("identity.mfa.admins", "admins.json"),
        });

        var summary = await runner.RunAsync(
            [Instance("first", "failing"), Instance("second", "throwing"), Instance("third", "idp")], new RunOptions(), TestContext.Current.CancellationToken);

        summary.Collectors.Select(c => c.Outcome).ShouldBe([CollectorOutcome.Failed, CollectorOutcome.Failed, CollectorOutcome.Succeeded]);
        summary.Collectors[0].Problems.ShouldHaveSingleItem().Code.ShouldBe("collector.unavailable");
        var crash = summary.Collectors[1].Problems.ShouldHaveSingleItem();
        crash.Code.ShouldBe("collector.crashed");
        crash.Message.ShouldContain("collector bug");
        _sink.Batches.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Exit_code_reflects_worst_outcome()
    {
        var ok = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("one", "idp")], new RunOptions(), TestContext.Current.CancellationToken);
        _sink.FailEverything = true;
        var publishFailed = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("two", "idp")], new RunOptions(), TestContext.Current.CancellationToken);

        ok.Outcome.ShouldBe(CollectorOutcome.Succeeded);
        publishFailed.Outcome.ShouldBe(CollectorOutcome.Failed);
        publishFailed.Collectors.Single().Problems.ShouldNotBeEmpty();
        publishFailed.Collectors.Single().Problems.ShouldAllBe(p => p.Code == "ciso_assistant.rejected");
        new RunSummary([]).Outcome.ShouldBe(CollectorOutcome.Succeeded);
    }

    [Fact]
    public async Task Dry_run_plans_without_sending()
    {
        var summary = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("one", "idp")], new RunOptions(DryRun: true), TestContext.Current.CancellationToken);

        _sink.Batches.ShouldBeEmpty();
        summary.Collectors.Single().Publish!.Planned.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "one")).ShouldBeTrue();
    }

    [Fact]
    public async Task No_publish_stores_evidence_and_plans_nothing()
    {
        var summary = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("one", "idp")], new RunOptions(NoPublish: true), TestContext.Current.CancellationToken);

        _sink.Batches.ShouldBeEmpty();
        summary.Collectors.Single().ShouldBe(new CollectorRunResult("one", CollectorOutcome.Succeeded, [], null));
        Directory.Exists(Path.Combine(_root, "one")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_plan_that_cannot_be_made_fails_that_collector()
    {
        var citesNothing = new StubCollector(context => Result.Ok(new CollectorOutput(
            [CheckResult.Create(Id("identity.mfa.admins"), CheckOutcome.Pass, "ok", ["not-collected.json"], context.Now, context.Name).Value()], [], [])));

        var summary = await Runner(new() { ["idp"] = citesNothing })
            .RunAsync([Instance("one", "idp")], new RunOptions(), TestContext.Current.CancellationToken);

        summary.Collectors.Single().Problems.ShouldHaveSingleItem().Code.ShouldBe("plan.evidence_unknown");
    }

    [Fact]
    public void Only_selects_one_collector_by_name()
    {
        IReadOnlyList<CollectorInstance> instances = [Instance("one", "idp"), Instance("two", "idp")];

        Runtime.Runner.Select(instances, "two").Value().ShouldHaveSingleItem().Name.ShouldBe("two");
        Runtime.Runner.Select(instances, null).Value().Count.ShouldBe(2);
        var unknown = Runtime.Runner.Select(instances, "three").Problems().ShouldHaveSingleItem();
        unknown.Code.ShouldBe("run.collector_unknown");
        unknown.Message.ShouldContain("one, two");
    }

    [Fact]
    public void Evidence_ids_name_the_collector_and_file()
    {
        RunEvidence.IdFor("example-idp", "admins.json").ShouldBe("example-idp/admins.json");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Records each batch of updates and reads every evidence file through the files it was given.</summary>
    private sealed class RecordingSink
    {
        public List<IReadOnlyList<AuditUpdate>> Batches { get; } = [];

        public bool FailEverything { get; set; }

        public IAuditUpdateSink With(IEvidenceFiles files) => new Sink(this, files);

        private sealed class Sink(RecordingSink owner, IEvidenceFiles files) : IAuditUpdateSink
        {
            public async Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken)
            {
                owner.Batches.Add(updates);
                var items = new List<PublishItem>();
                foreach (var update in updates)
                {
                    var outcome = owner.FailEverything
                        ? Result.Fail<PublishOutcome>(new Problem("ciso_assistant.rejected", "refused"))
                        : await update.Match(
                            upload => ReadAsync(upload.Evidence, cancellationToken),
                            _ => Task.FromResult(Result.Ok(PublishOutcome.Sent)),
                            _ => Task.FromResult(Result.Ok(PublishOutcome.Sent)));
                    items.Add(PublishItem.For(update, outcome));
                }

                return new PublishReport(items);
            }

            private async Task<Result<PublishOutcome>> ReadAsync(Evidence evidence, CancellationToken cancellationToken) =>
                (await EvidenceContent.ReadVerifiedAsync(files, evidence, cancellationToken)).Map(_ => PublishOutcome.Sent);
        }
    }
}
