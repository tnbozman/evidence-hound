using System.Text;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Pipeline;
using EvidenceHound.Runtime.Status;
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

    private static StubCollector Reporting(string checkId, string file) => new(context => Result.Ok(new CollectionResult(
        [CheckResult.Create(Id(checkId), CheckOutcome.Pass, "ok", [file], context.Clock.UtcNow, context.Name).Value()],
        [CollectedEvidence.FromBytes(file, "application/json", Encoding.UTF8.GetBytes("{\"ok\": true}"))],
        [])));

    private Runner Runner(Dictionary<string, ICollector> collectors) => new(
        collectors,
        new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), new FixedClock(Now), retentionDays: null, TestCollectorServices.None),
        Mapping,
        Target,
        files => _sink.With(files),
        new FixedClock(Now),
        new FileRunRecordStore(_root),
        "AC-AUTO");

    [Fact]
    public async Task Runs_each_configured_collector()
    {
        var runner = Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json"), ["backup"] = Reporting("backup.daily", "jobs.json") });

        var summary = await runner.RunAsync([Instance("example-idp", "idp"), Instance("example-backup", "backup")], new RunOptions(), TestContext.Current.CancellationToken);

        summary.Collectors.Select(c => (c.Collector, c.Outcome)).ShouldBe([("example-idp", CollectorOutcome.Succeeded), ("example-backup", CollectorOutcome.Succeeded)]);
        _sink.Main.Count.ShouldBe(2);
        _sink.Main[0].Select(u => u.GetType().Name).ShouldBe(["UploadEvidence", "UpdateRequirementAssessment"]);
        _sink.Main[0].OfType<AuditUpdate.UploadEvidence>().Single().Evidence.Id.ShouldBe("example-idp/admins.json");
        var requirement = _sink.Main[0].OfType<AuditUpdate.UpdateRequirementAssessment>().Single();
        (requirement.ComplianceAssessmentRefId, requirement.RequirementRefId, requirement.Result).ShouldBe(("EXAMPLE-1", "R-MFA", ComplianceResult.Compliant));
        requirement.EvidenceRefIds.ShouldBe(["example-idp/admins.json"]);
        _sink.Main[1].OfType<AuditUpdate.UpdateRequirementAssessment>().Single().RequirementRefId.ShouldBe("R-BACKUP");
        summary.Collectors[0].Publish!.Sent.ShouldBe(2);
    }

    [Fact]
    public async Task Owned_results_go_to_every_target_with_one_evidence_upload()
    {
        var second = FrameworkMapping.Create("urn:example:y", [RequirementMapping.Create("Q-MFA", [Id("identity.mfa.admins")], MappingRule.AllPass).Value()]).Value();
        var runner = new Runner(
            new Dictionary<string, ICollector> { ["idp"] = Reporting("identity.mfa.admins", "admins.json") },
            new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), new FixedClock(Now), retentionDays: null, TestCollectorServices.None),
            [new RunTarget(Mapping, Target, "AC-AUTO"), new RunTarget(second, new AuditTarget("EXAMPLE-2", new Dictionary<string, string>()), "AC-AUTO-2")],
            files => _sink.With(files),
            new FixedClock(Now),
            new FileRunRecordStore(_root));

        var summary = await runner.RunAsync([Instance("example-idp", "idp")], new RunOptions(), TestContext.Current.CancellationToken);

        summary.Outcome.ShouldBe(CollectorOutcome.Succeeded);
        var batch = _sink.Main.ShouldHaveSingleItem();
        batch.OfType<AuditUpdate.UploadEvidence>().ShouldHaveSingleItem();
        batch.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => (u.ComplianceAssessmentRefId, u.RequirementRefId)).ShouldBe([("EXAMPLE-1", "R-MFA"), ("EXAMPLE-2", "Q-MFA")]);
        _sink.HealthUploads.ShouldHaveSingleItem().AppliedControlRefIds.ShouldBe(["AC-AUTO", "AC-AUTO-2"]);
    }

    [Fact]
    public async Task Failure_in_one_collector_continues_others()
    {
        var failing = new StubCollector(_ => Result.Fail<CollectionResult>(new Problem("collector.unavailable", "the system did not answer")));
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
        _sink.Main.ShouldHaveSingleItem();
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

        _sink.Main.ShouldBeEmpty();
        summary.Collectors.Single().Publish!.Planned.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "one")).ShouldBeTrue();
    }

    [Fact]
    public async Task No_publish_stores_evidence_and_plans_nothing()
    {
        var summary = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("one", "idp")], new RunOptions(NoPublish: true), TestContext.Current.CancellationToken);

        _sink.Main.ShouldBeEmpty();
        summary.Collectors.Single().ShouldBe(new CollectorRunResult("one", CollectorOutcome.Succeeded, [], null));
        _sink.Batches.ShouldBeEmpty();
        Directory.Exists(Path.Combine(_root, "one")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_plan_that_cannot_be_made_fails_that_collector()
    {
        var citesNothing = new StubCollector(context => Result.Ok(new CollectionResult(
            [CheckResult.Create(Id("identity.mfa.admins"), CheckOutcome.Pass, "ok", ["not-collected.json"], context.Clock.UtcNow, context.Name).Value()], [], [])));

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

    [Fact]
    public async Task Health_evidence_planned_after_each_run()
    {
        var failing = new StubCollector(_ => Result.Fail<CollectionResult>(new Problem("collector.unavailable", "the system did not answer")));
        var runner = Runner(new() { ["failing"] = failing, ["idp"] = Reporting("identity.mfa.admins", "admins.json") });

        var summary = await runner.RunAsync([Instance("first", "failing"), Instance("second", "idp")], new RunOptions(), TestContext.Current.CancellationToken);

        _sink.HealthUploads.Select(u => u.Evidence.Id).ShouldBe(["hound-health/first", "hound-health/second"]);
        _sink.HealthUploads.ShouldAllBe(u => u.AppliedControlRefIds.Count == 1 && u.AppliedControlRefIds[0] == "AC-AUTO");
        summary.Collectors.Select(c => c.Health!.Sent).ShouldBe([1, 1]);
        _sink.Batches.Select(b => b[0] is AuditUpdate.UploadEvidence u && u.Evidence.Id.StartsWith("hound-health/", StringComparison.Ordinal) ? u.Evidence.Id : "main").ShouldBe(["hound-health/first", "main", "hound-health/second"]);
    }

    [Fact]
    public async Task Dry_run_plans_the_health_evidence_without_sending_it()
    {
        var summary = await Runner(new() { ["idp"] = Reporting("identity.mfa.admins", "admins.json") })
            .RunAsync([Instance("one", "idp")], new RunOptions(DryRun: true), TestContext.Current.CancellationToken);

        _sink.Batches.ShouldBeEmpty();
        summary.Collectors.Single().Health!.Planned.ShouldBe(1);
    }

    [Fact]
    public async Task Each_run_saves_its_run_record()
    {
        var failing = new StubCollector(_ => Result.Fail<CollectionResult>(new Problem("collector.unavailable", "the system did not answer")));
        var runner = Runner(new() { ["failing"] = failing, ["idp"] = Reporting("identity.mfa.admins", "admins.json") });

        var summary = await runner.RunAsync([Instance("first", "failing"), Instance("second", "idp")], new RunOptions(NoPublish: true), TestContext.Current.CancellationToken);

        var saved = (await new FileRunRecordStore(_root).LatestAsync(TestContext.Current.CancellationToken)).Value();
        saved.Select(r => (r.Collector, r.Outcome, r.ChecksPassed)).ShouldBe([("first", CollectorOutcome.Failed, 0), ("second", CollectorOutcome.Succeeded, 1)]);
        saved[0].Problems.ShouldHaveSingleItem().Code.ShouldBe("collector.unavailable");
        summary.Collectors.Select(c => c.Record).ShouldBe(saved);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Records each batch of updates and reads every evidence file through the files it was given.</summary>
    private sealed class RecordingSink
    {
        public List<IReadOnlyList<AuditUpdate>> Batches { get; } = [];

        /// <summary>Batches other than health evidence uploads.</summary>
        public List<IReadOnlyList<AuditUpdate>> Main =>
            [.. Batches.Where(b => !b.All(u => u is AuditUpdate.UploadEvidence upload && upload.Evidence.Id.StartsWith("hound-health/", StringComparison.Ordinal)))];

        public List<AuditUpdate.UploadEvidence> HealthUploads =>
            [.. Batches.SelectMany(b => b).OfType<AuditUpdate.UploadEvidence>().Where(u => u.Evidence.Id.StartsWith("hound-health/", StringComparison.Ordinal))];

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
