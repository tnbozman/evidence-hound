using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Status;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime.Tests;

public sealed class HistoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("hound-history-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static CheckId Id(string id) => CheckId.Create(id).Value();

    private static readonly FrameworkMapping First = FrameworkMapping.Create("urn:example:x", [
        RequirementMapping.Create("R-MFA", [Id("identity.mfa.admins")], MappingRule.AllPass).Value(),
        RequirementMapping.Create("R-BACKUP", [Id("backup.daily")], MappingRule.AllPass).Value(),
    ]).Value();

    private static readonly FrameworkMapping Second = FrameworkMapping.Create("urn:example:y", [
        RequirementMapping.Create("Q-MFA", [Id("identity.mfa.admins")], MappingRule.AllPass).Value(),
    ]).Value();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Run_appends_one_line_per_audit()
    {
        var runner = Runner();

        await runner.RunAsync([Instance("example-idp", "idp"), Instance("example-backup", "backup")], new RunOptions(), _ct);

        var first = Lines("EXAMPLE-1").ShouldHaveSingleItem();
        first["date"]!.GetValue<DateTimeOffset>().ShouldBe(Now);
        first["run_id"]!.GetValue<string>().ShouldBe("20260630T020000Z");
        (first["changed"]!.GetValue<int>(), first["unchanged"]!.GetValue<int>(), first["drift"]!.GetValue<int>()).ShouldBe((2, 0, 0));
        Lines("EXAMPLE-2").ShouldHaveSingleItem()["changed"]!.GetValue<int>().ShouldBe(1);

        await runner.RunAsync([Instance("example-idp", "idp"), Instance("example-backup", "backup")], new RunOptions(), _ct);

        Lines("EXAMPLE-1").Count.ShouldBe(2);
        Lines("EXAMPLE-2").Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dry_run_and_no_publish_write_no_history()
    {
        await Runner().RunAsync([Instance("example-idp", "idp")], new RunOptions(DryRun: true), _ct);
        await Runner().RunAsync([Instance("example-idp", "idp")], new RunOptions(NoPublish: true), _ct);

        Directory.Exists(Path.Combine(_root, "_history")).ShouldBeFalse();
    }

    [Fact]
    public async Task Latest_entry_of_each_audit_is_read_back()
    {
        var history = new FileRunHistory(_root);
        await history.AppendAsync("EXAMPLE-1", new HistoryEntry(Now.AddDays(-1), "20260629T020000Z", 3, 0, 0), _ct);
        await history.AppendAsync("EXAMPLE-1", new HistoryEntry(Now, "20260630T020000Z", 1, 2, 1), _ct);
        await history.AppendAsync("EXAMPLE-2", new HistoryEntry(Now, "20260630T020000Z", 0, 4, 0), _ct);

        var latest = await history.LatestAsync(_ct);

        latest.Value().ShouldBe([
            ("EXAMPLE-1", new HistoryEntry(Now, "20260630T020000Z", 1, 2, 1)),
            ("EXAMPLE-2", new HistoryEntry(Now, "20260630T020000Z", 0, 4, 0)),
        ]);
    }

    [Fact]
    public async Task Run_reads_the_targets_current_state_and_names_the_run_in_each_written_row()
    {
        var states = new RecordingStates();
        var sink = new AcceptingSink();
        var runner = Runner(sink, states);

        var summary = await runner.RunAsync([Instance("example-idp", "idp")], new RunOptions(), _ct);

        var query = states.Queries.ShouldHaveSingleItem();
        query.AuditRefIds.ShouldBe(["EXAMPLE-1", "EXAMPLE-2"]);
        query.EvidenceRefIds.ShouldBe(["example-idp/admins.json"]);
        summary.RunId.ShouldBe("20260630T020000Z");
        sink.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldAllBe(u => u.Observation.Contains("[hound run 20260630T020000Z state "));
        summary.Collectors[0].Changes.ShouldBe([new AuditChanges("EXAMPLE-1", 1, 0, 0), new AuditChanges("EXAMPLE-2", 1, 0, 0)]);
        summary.Audits.ShouldBe([new AuditChanges("EXAMPLE-1", 1, 0, 0), new AuditChanges("EXAMPLE-2", 1, 0, 0)]);
    }

    private List<JsonNode> Lines(string audit) =>
        [.. File.ReadAllLines(Path.Combine(_root, "_history", $"{audit}.jsonl")).Select(line => JsonNode.Parse(line)!)];

    private Runner Runner(AcceptingSink? sink = null, ICurrentStateSource? states = null)
    {
        var accepting = sink ?? new AcceptingSink();
        return new Runner(
            new Dictionary<string, ICollector> { ["idp"] = Reporting("identity.mfa.admins", "admins.json"), ["backup"] = Reporting("backup.daily", "jobs.json") },
            new CollectStep(Secrets.From(new Dictionary<string, string>()), new FileEvidenceStore(_root), new FixedClock(Now), retentionDays: null, TestCollectorServices.None),
            [
                new RunTarget(First, new AuditTarget("EXAMPLE-1", new Dictionary<string, string>())),
                new RunTarget(Second, new AuditTarget("EXAMPLE-2", new Dictionary<string, string>())),
            ],
            _ => accepting,
            new FixedClock(Now),
            new FileRunRecordStore(_root))
        {
            States = states ?? NoCurrentState.Instance,
            History = new FileRunHistory(_root),
        };
    }

    private static CollectorInstance Instance(string name, string type) =>
        new(name, type, "0 2 * * *", new Dictionary<string, string>(), new Dictionary<string, SecretReference>());

    private static StubCollector Reporting(string checkId, string file) => new(context => Result.Ok(new CollectionResult(
        [CheckResult.Create(Id(checkId), CheckOutcome.Pass, "ok", [file], context.Clock.UtcNow, context.Name).Value()],
        [CollectedEvidence.FromBytes(file, "application/json", Encoding.UTF8.GetBytes("{\"ok\": true}"))],
        [])));

    private sealed class RecordingStates : ICurrentStateSource
    {
        public List<StateQuery> Queries { get; } = [];

        public Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(CurrentState.Empty);
        }
    }

    private sealed class AcceptingSink : IAuditUpdateSink
    {
        public List<AuditUpdate> Updates { get; } = [];

        public Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken)
        {
            Updates.AddRange(updates);
            return Task.FromResult(new PublishReport([.. updates.Select(u => PublishItem.For(u, Result.Ok(PublishOutcome.Sent)))]));
        }
    }
}
