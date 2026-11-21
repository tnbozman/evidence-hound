using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Tests.Checks;

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

    private static IReadOnlyList<AuditUpdate> Plan(RequirementResult[] results, Evidence[] evidence, ControlStatus[] controls) =>
        PlanUpdates(results, evidence, controls).Value();

    private static Result<IReadOnlyList<AuditUpdate>> PlanUpdates(RequirementResult[] results, Evidence[] evidence, ControlStatus[] controls) =>
        AuditPlanner.PlanUpdates(Target, results, evidence, controls);

    private static RequirementResult Requirement(string reference, ComplianceResult result, params string[] evidence) =>
        new(reference, result, $"Observed {reference}.", evidence);

    private static Evidence Evidence(string id, string fileName) =>
        global::EvidenceHound.Core.Checks.Evidence.Create(id, fileName, "application/octet-stream", CheckTestData.Sha256, 7, CheckTestData.CollectedAt).Value();
}
