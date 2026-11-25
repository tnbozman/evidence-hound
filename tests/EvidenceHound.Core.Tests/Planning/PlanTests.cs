using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Tests.Checks;
using EvidenceHound.Core.Tests.Publishing;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Planning;

public class PlanTests
{
    private static readonly AuditTarget Target = new(
        "EXAMPLE-BASELINE-2026",
        new Dictionary<string, string> { ["mfa"] = "AC-MFA", ["backups"] = "AC-BACKUP" });

    private static readonly Evidence MfaAdmins = Evidence("ev-0001", "mfa-admins.json");
    private static readonly Evidence BackupJobs = Evidence("ev-0002", "backup-jobs.csv");

    [Fact]
    public void Evidence_uploads_precede_requirement_updates()
    {
        var plan = Plan(
            [Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0002"), Requirement("EX-1.2", ComplianceResult.NonCompliant, "ev-0001")],
            [BackupJobs, MfaAdmins],
            [new ControlStatus("mfa", ControlState.Active)]);

        plan.Select(u => u.GetType().Name).ShouldBe([
            "UploadEvidence", "UploadEvidence", "UpdateRequirementAssessment", "UpdateRequirementAssessment", "UpdateAppliedControl",
        ]);
        plan.OfType<AuditUpdate.UploadEvidence>().Select(u => u.Evidence.Id).ShouldBe(["ev-0001", "ev-0002"]);
    }

    [Fact]
    public void One_update_per_requirement_result()
    {
        var plan = Plan(
            [
                Requirement("EX-2.1", ComplianceResult.NotAssessed),
                Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0001"),
                Requirement("EX-1.2", ComplianceResult.NotApplicable),
            ],
            [MfaAdmins],
            []);

        var updates = plan.OfType<AuditUpdate.UpdateRequirementAssessment>().ToArray();
        updates.Select(u => (u.ComplianceAssessmentRefId, u.RequirementRefId, u.Result)).ShouldBe([
            ("EXAMPLE-BASELINE-2026", "EX-2.1", ComplianceResult.NotAssessed),
            ("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant),
            ("EXAMPLE-BASELINE-2026", "EX-1.2", ComplianceResult.NotApplicable),
        ]);
        updates[1].EvidenceRefIds.ShouldBe(["ev-0001"]);
    }

    [Fact]
    public void Control_updates_use_the_configured_ref_ids_in_key_order()
    {
        var plan = Plan([], [], [new ControlStatus("mfa", ControlState.InProgress), new ControlStatus("backups", ControlState.Active)]);

        plan.OfType<AuditUpdate.UpdateAppliedControl>().Select(u => (u.RefId, u.Status)).ShouldBe([
            ("AC-BACKUP", ControlState.Active),
            ("AC-MFA", ControlState.InProgress),
        ]);
    }

    [Fact]
    public void Two_targets_share_one_evidence_upload()
    {
        var e8 = new AuditTarget("EX-E8-2026", new Dictionary<string, string> { ["mfa"] = "AC-MFA" });
        var essentials = new AuditTarget("EX-ESSENTIALS-2026", new Dictionary<string, string> { ["mfa"] = "AC-MFA" });

        var plan = AuditPlanner.PlanUpdates(
            [
                new TargetResults(e8, [Requirement("ISM-1504", ComplianceResult.Compliant, "ev-0001")]),
                new TargetResults(essentials, [Requirement("IT.IDE-01.1", ComplianceResult.Compliant, "ev-0001")]),
            ],
            [MfaAdmins],
            [new ControlStatus("mfa", ControlState.Active)]).Value();

        plan.OfType<AuditUpdate.UploadEvidence>().ShouldHaveSingleItem().Evidence.Id.ShouldBe("ev-0001");
        plan.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => (u.ComplianceAssessmentRefId, u.RequirementRefId, string.Join(",", u.EvidenceRefIds))).ShouldBe([
            ("EX-E8-2026", "ISM-1504", "ev-0001"),
            ("EX-ESSENTIALS-2026", "IT.IDE-01.1", "ev-0001"),
        ]);
        plan.OfType<AuditUpdate.UpdateAppliedControl>().ShouldHaveSingleItem().RefId.ShouldBe("AC-MFA");
    }

    [Fact]
    public void Planning_every_update_for_a_run_marks_each_observation_as_planning_only_changes_does()
    {
        var target = new AuditTarget("EX-E8-2026", new Dictionary<string, string> { ["mfa"] = "AC-MFA" });
        TargetResults[] targets = [new(target, [Requirement("ISM-1504", ComplianceResult.Compliant, "ev-0001"), Requirement("ISM-1173", ComplianceResult.NonCompliant)])];

        var every = AuditPlanner.PlanUpdates(targets, [MfaAdmins], [], Run1).Value();
        var changes = AuditPlanner.PlanChanges(targets, [MfaAdmins], [], new CurrentState([], [], []), Run1).Value();

        var observations = every.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => u.Observation).ToArray();
        observations.ShouldBe(changes.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => u.Observation));
        observations.ShouldAllBe(o => RowState.ReadMark(o)!.RunId == Run1);
    }

    [Fact]
    public void A_control_key_configured_by_any_target_is_known()
    {
        var first = new AuditTarget("EX-A", new Dictionary<string, string> { ["mfa"] = "AC-MFA" });
        var second = new AuditTarget("EX-B", new Dictionary<string, string> { ["backups"] = "AC-BACKUP" });

        var plan = AuditPlanner.PlanUpdates(
            [new TargetResults(first, []), new TargetResults(second, [])],
            [],
            [new ControlStatus("backups", ControlState.Active), new ControlStatus("mfa", ControlState.OnHold)]).Value();

        plan.OfType<AuditUpdate.UpdateAppliedControl>().Select(u => u.RefId).ShouldBe(["AC-BACKUP", "AC-MFA"]);
    }

    [Fact]
    public void Unknown_control_key_and_unknown_evidence_ref_fail()
    {
        var plan = PlanUpdates(
            [Requirement("EX-1.1", ComplianceResult.Compliant, "ev-9999")],
            [MfaAdmins],
            [new ControlStatus("firewall", ControlState.Active)]);

        plan.Problems().Select(p => p.Code).ShouldBe(["plan.evidence_unknown", "plan.control_key_unknown"]);
        plan.Problems()[0].Message.ShouldContain("ev-9999");
        plan.Problems()[1].Message.ShouldContain("firewall");
        // s14-03 rehearsal: after `hound add collector fake` the next suggested run failed, and the message did not say where to fix it.
        plan.Problems()[1].Message.ShouldContain("controls:");
    }

    [Theory]
    [InlineData("upload_attachment")]
    [InlineData("update_requirement_assessment")]
    [InlineData("update_applied_control")]
    public void Serialises_to_dispatcher_message_shape(string messageType)
    {
        AuditUpdate update = messageType switch
        {
            "upload_attachment" => new AuditUpdate.UploadEvidence(MfaAdmins),
            "update_requirement_assessment" => new AuditUpdate.UpdateRequirementAssessment(
                "EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.PartiallyCompliant, "Rule all_pass. identity.mfa.admins: pass (All 4 admins use MFA.)", ["ev-0001"]),
            _ => new AuditUpdate.UpdateAppliedControl("AC-MFA", ControlState.InProgress),
        };
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", $"{messageType}.json")));

        var message = DispatcherMessages.ToJson(update, _ => "ZXhhbXBsZQ==");

        JsonNode.DeepEquals(message, golden).ShouldBeTrue(message.ToJsonString());
    }

    [Fact]
    public void Every_control_state_has_its_ciso_assistant_name()
    {
        Enum.GetValues<ControlState>()
            .Select(s => DispatcherMessages.ToJson(new AuditUpdate.UpdateAppliedControl("AC-1", s), _ => "")["values"]!["status"]!.GetValue<string>())
            .ShouldBe(["to_do", "in_progress", "on_hold", "active", "deprecated"]);
    }

    [Fact]
    public void Undefined_control_state_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DispatcherMessages.ToJson(new AuditUpdate.UpdateAppliedControl("AC-1", (ControlState)99), _ => ""));
    }

    [Fact]
    public void Unchanged_row_is_not_sent()
    {
        RequirementResult[] results = [Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0001") with { CheckOutcomes = ["identity.mfa.admins:pass"] }];
        ControlStatus[] controls = [new ControlStatus("mfa", ControlState.Active)];
        var first = Changes(results, [MfaAdmins], controls, Fresh("EX-1.1"), Run1);

        var second = Changes(results, [MfaAdmins], controls, Published(Fresh("EX-1.1"), first), Run2);

        first.Updates.Count.ShouldBe(3);
        first.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 1, Unchanged: 0, Drift: 0)]);
        second.Updates.ShouldBeEmpty();
        second.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 0, Unchanged: 1, Drift: 0)]);
        second.Drift.ShouldBeEmpty();
    }

    [Fact]
    public void Changed_check_outcome_with_same_result_is_sent()
    {
        var before = Requirement("EX-1.1", ComplianceResult.Compliant) with { CheckOutcomes = ["identity.mfa.admins:pass", "identity.mfa.users:fail"] };
        var after = before with { CheckOutcomes = ["identity.mfa.admins:fail", "identity.mfa.users:pass"], Observation = "Rule any_pass. Now the users pass." };
        var state = Published(Fresh("EX-1.1"), Changes([before], [], [], Fresh("EX-1.1"), Run1));

        var second = Changes([after], [], [], state, Run2);

        var update = second.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem();
        update.RowIds.ShouldBe(["ra-EX-1.1"]);
        update.Result.ShouldBe(ComplianceResult.Compliant);
        update.Observation.ShouldStartWith(after.Observation);
        update.Observation.ShouldContain(Run2);
        second.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 1, Unchanged: 0, Drift: 0)]);
    }

    [Fact]
    public void Timestamp_only_change_is_not_sent()
    {
        RequirementResult[] results = [Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0001") with { CheckOutcomes = ["identity.mfa.admins:pass"] }];
        var today = Evidence("ev-0001", "mfa-admins.json", CheckTestData.CollectedAt.AddDays(1));
        var state = Published(Fresh("EX-1.1"), Changes(results, [MfaAdmins], [], Fresh("EX-1.1"), Run1));

        var second = Changes(results, [today], [], state, Run2);

        second.Updates.ShouldBeEmpty();
    }

    [Fact]
    public void Known_evidence_hash_is_linked_not_uploaded()
    {
        var state = Fresh("EX-1.1") with { Evidence = [new CurrentEvidence("ev-0001", "uuid-ev-0001", CheckTestData.Sha256)] };

        var plan = Changes([Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0001")], [MfaAdmins], [], state, Run1);

        plan.Updates.OfType<AuditUpdate.UploadEvidence>().ShouldBeEmpty();
        var update = plan.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem();
        update.EvidenceRefIds.ShouldBe(["ev-0001"]);
        update.KnownEvidenceIds.ShouldBe(new Dictionary<string, string> { ["ev-0001"] = "uuid-ev-0001" });
    }

    [Fact]
    public void Changed_evidence_hash_is_uploaded()
    {
        var state = Fresh("EX-1.1") with { Evidence = [new CurrentEvidence("ev-0001", "uuid-ev-0001", new string('0', 64))] };

        var plan = Changes([Requirement("EX-1.1", ComplianceResult.Compliant, "ev-0001")], [MfaAdmins], [], state, Run1);

        plan.Updates.OfType<AuditUpdate.UploadEvidence>().ShouldHaveSingleItem().Evidence.Id.ShouldBe("ev-0001");
        plan.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem().KnownEvidenceIds.ShouldBeEmpty();
    }

    [Fact]
    public void Row_edited_in_ui_is_restored_and_reported_as_drift()
    {
        RequirementResult[] results = [Requirement("EX-1.1", ComplianceResult.Compliant), Requirement("EX-1.2", ComplianceResult.NonCompliant)];
        var state = Published(Fresh("EX-1.1", "EX-1.2"), Changes(results, [], [], Fresh("EX-1.1", "EX-1.2"), Run1));
        var audit = state.Audits[0];
        var edited = state with { Audits = [audit with { Rows = [audit.Rows[0] with { Result = ComplianceResult.NonCompliant }, audit.Rows[1]] }] };

        var second = Changes(results, [], [], edited, Run2);

        var update = second.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem();
        update.RowIds.ShouldBe(["ra-EX-1.1"]);
        update.Result.ShouldBe(ComplianceResult.Compliant);
        var drift = second.Drift.ShouldHaveSingleItem();
        drift.Code.ShouldBe("publish.drift_corrected");
        drift.Message.ShouldContain("EXAMPLE-BASELINE-2026");
        drift.Message.ShouldContain("urn:example:req:ex-1.1");
        drift.Message.ShouldContain("was non_compliant");
        drift.Message.ShouldContain("now compliant");
        second.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 1, Unchanged: 1, Drift: 1)]);
    }

    [Fact]
    public void Rows_outside_the_selected_groups_are_neither_sent_nor_counted()
    {
        var state = new CurrentState(
            [new CurrentAudit(Target.ComplianceAssessmentRefId, ["1"], [Row("EX-1.1") with { Groups = ["2"] }])], [], []);

        var plan = Changes([Requirement("EX-1.1", ComplianceResult.Compliant)], [], [], state, Run1);

        plan.Updates.ShouldBeEmpty();
        plan.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 0, Unchanged: 0, Drift: 0)]);
    }

    [Fact]
    public void A_requirement_with_no_known_row_is_left_for_the_publisher_to_find()
    {
        var plan = Changes([Requirement("EX-9.9", ComplianceResult.Compliant)], [], [], Fresh("EX-1.1"), Run1);

        var update = plan.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem();
        update.RowIds.ShouldBeEmpty();
        update.Observation.ShouldContain(Run1);
        plan.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 1, Unchanged: 0, Drift: 0)]);
    }

    [Property(MaxTest = 50)]
    public async Task Planning_against_the_state_that_publishing_the_plan_produces_is_empty((byte Result, byte Outcome, bool CitesEvidence)[] picks)
    {
        var ct = TestContext.Current.CancellationToken;
        RequirementResult[] results = [.. (picks ?? []).Take(5).Select((p, i) => Requirement(
            $"EX-{i + 1}", (ComplianceResult)(p.Result % 5), p.CitesEvidence ? ["ev-0001"] : []) with { CheckOutcomes = [$"example.check_{i}:{p.Outcome % 3}"] })];
        ControlStatus[] controls = [new ControlStatus("mfa", ControlState.Active)];
        var ca = new InMemoryCisoAssistant();
        ca.Rows.AddRange(results.Select(r => new RemoteRequirementAssessment($"ra-{r.RequirementRef}", $"urn:example:req:{r.RequirementRef}", r.RequirementRef, [])));
        ca.Controls["AC-MFA"] = ControlState.ToDo;
        var files = new InMemoryEvidenceFiles(new() { ["ev-0001"] = Encoding.UTF8.GetBytes("test") });
        TargetResults[] targets = [new TargetResults(Target, results)];
        var query = StateQuery.For(targets, [MfaAdmins], controls);
        var states = new ClientStateSource(ca);

        var plan = AuditPlanner.PlanChanges(targets, [MfaAdmins], controls, await states.ReadAsync(query, ct), Run1).Value();
        await new Publisher(ca, files).PublishAsync(plan.Updates, ct);
        var replan = AuditPlanner.PlanChanges(targets, [MfaAdmins], controls, await states.ReadAsync(query, ct), Run2).Value();

        replan.Updates.ShouldBeEmpty();
        replan.Audits.ShouldBe([new AuditChanges("EXAMPLE-BASELINE-2026", Changed: 0, Unchanged: results.Length, Drift: 0)]);
    }

    [Fact]
    public async Task State_source_reads_the_scope_documents()
    {
        var ca = new InMemoryCisoAssistant();
        ca.ScopeDocumentIds.Add("uuid-policy");

        var state = await new ClientStateSource(ca).ReadAsync(new StateQuery([], [], []), TestContext.Current.CancellationToken);

        state.ScopeDocumentIds.ShouldBe(["uuid-policy"]);
    }

    [Fact]
    public void Scope_documents_on_a_row_are_kept_and_are_not_drift()
    {
        RequirementResult[] results = [Requirement("EX-1.1", ComplianceResult.Compliant)];
        var state = Published(Fresh("EX-1.1"), Changes(results, [], [], Fresh("EX-1.1"), Run1));
        var audit = state.Audits[0];
        var linked = state with { Audits = [audit with { Rows = [audit.Rows[0] with { EvidenceIds = ["uuid-policy"] }] }], ScopeDocumentIds = ["uuid-policy"] };

        var unchanged = Changes(results, [], [], linked, Run2);
        var changed = Changes([Requirement("EX-1.1", ComplianceResult.NonCompliant)], [], [], linked, Run2);

        (unchanged.Updates.Count, unchanged.Drift.Count).ShouldBe((0, 0));
        var kept = changed.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem().KeptEvidenceIds;
        kept.Keys.ShouldBe(["ra-EX-1.1"]);
        kept["ra-EX-1.1"].ShouldBe(["uuid-policy"]);
    }

    [Fact]
    public void Audit_in_review_or_locked_is_never_written()
    {
        var closed = Fresh("EX-1.1") with { Audits = [Fresh("EX-1.1").Audits[0] with { Closed = true }] };

        var plan = Changes([Requirement("EX-1.1", ComplianceResult.Compliant)], [], [], closed, Run1);

        plan.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldBeEmpty();
        plan.Drift.ShouldHaveSingleItem().Code.ShouldBe("plan.audit_closed");
    }

    [Fact]
    public void Failed_check_under_approved_exception_names_it_in_observation()
    {
        // s15-03: an approved, unexpired security exception covers the requirement, so the observation says so; the result stands.
        var covered = Row("EX-1.1") with { Exceptions = [new RowWaiver("EXC-01", "Legacy finance app without MFA", "approved", new DateOnly(2027, 3, 31))] };
        var state = new CurrentState([new CurrentAudit(Target.ComplianceAssessmentRefId, [], [covered])], [], []) { Today = new DateOnly(2026, 11, 25) };

        var update = Changes([Requirement("EX-1.1", ComplianceResult.NonCompliant)], [], [], state, Run1).Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().ShouldHaveSingleItem();

        update.Result.ShouldBe(ComplianceResult.NonCompliant);
        update.Observation.ShouldContain("security exception EXC-01 (Legacy finance app without MFA), approved until 2027-03-31");

        var passing = Changes([Requirement("EX-1.1", ComplianceResult.Compliant)], [], [], state, Run1).Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Single();
        passing.Observation.ShouldNotContain("EXC-01", customMessage: "a passing check needs no exception");
        var expired = state with { Today = new DateOnly(2027, 4, 1) };
        Changes([Requirement("EX-1.1", ComplianceResult.NonCompliant)], [], [], expired, Run1).Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Single()
            .Observation.ShouldNotContain("EXC-01", customMessage: "an expired exception covers nothing");
        var draft = new CurrentState([new CurrentAudit(Target.ComplianceAssessmentRefId, [], [covered with { Exceptions = [covered.Exceptions[0] with { Status = "draft" }] }])], [], []);
        Changes([Requirement("EX-1.1", ComplianceResult.NonCompliant)], [], [], draft, Run1).Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Single()
            .Observation.ShouldNotContain("EXC-01", customMessage: "only an approved exception counts");
    }

    private static readonly string Run1 = "20261120T020000Z";
    private static readonly string Run2 = "20261121T020000Z";

    private static ChangePlan Changes(RequirementResult[] results, Evidence[] evidence, ControlStatus[] controls, CurrentState current, string runId) =>
        AuditPlanner.PlanChanges([new TargetResults(Target, results)], evidence, controls, current, runId).Value();

    private static CurrentState Fresh(params string[] references) =>
        new([new CurrentAudit(Target.ComplianceAssessmentRefId, [], [.. references.Select(Row)])], [], []);

    private static CurrentRow Row(string reference) =>
        new($"ra-{reference}", $"urn:example:req:{reference.ToLowerInvariant()}", reference, [], ComplianceResult.NotAssessed, "", []);

    /// <summary>The state CISO Assistant would hold after the plan is published to <paramref name="state"/>: evidence gets ID <c>uuid-&lt;ref_id&gt;</c>.</summary>
    private static CurrentState Published(CurrentState state, ChangePlan plan)
    {
        var uploaded = plan.Updates.OfType<AuditUpdate.UploadEvidence>().Select(u => new CurrentEvidence(u.Evidence.Id, $"uuid-{u.Evidence.Id}", u.Evidence.Sha256)).ToArray();
        CurrentEvidence[] evidence = [.. state.Evidence.Where(e => uploaded.All(u => u.RefId != e.RefId)), .. uploaded];
        var ids = evidence.ToDictionary(e => e.RefId, e => e.Id);
        var writes = plan.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().SelectMany(u => u.RowIds.Select(id => (Id: id, Update: u))).ToDictionary(w => w.Id, w => w.Update);
        var controls = plan.Updates.OfType<AuditUpdate.UpdateAppliedControl>().Select(c => new CurrentControl(c.RefId, c.Status)).ToArray();
        return new CurrentState(
            [.. state.Audits.Select(a => a with
            {
                Rows = [.. a.Rows.Select(r => writes.TryGetValue(r.Id, out var u)
                    ? r with { Result = u.Result, Observation = u.Observation, EvidenceIds = [.. u.EvidenceRefIds.Select(refId => ids[refId])] }
                    : r)],
            })],
            evidence,
            [.. state.Controls.Where(c => controls.All(n => n.RefId != c.RefId)), .. controls]);
    }

    private static IReadOnlyList<AuditUpdate> Plan(RequirementResult[] results, Evidence[] evidence, ControlStatus[] controls) =>
        PlanUpdates(results, evidence, controls).Value();

    private static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(RequirementResult[] results, Evidence[] evidence, ControlStatus[] controls) =>
        AuditPlanner.PlanUpdates(Target, results, evidence, controls);

    private static RequirementResult Requirement(string reference, ComplianceResult result, params string[] evidence) =>
        new(reference, result, $"Observed {reference}.", evidence);

    private static Evidence Evidence(string id, string fileName) => Evidence(id, fileName, CheckTestData.CollectedAt);

    private static Evidence Evidence(string id, string fileName, DateTimeOffset collectedAt) =>
        global::EvidenceHound.Core.Checks.Evidence.Create(id, fileName, "application/octet-stream", CheckTestData.Sha256, 7, collectedAt).Value();
}
