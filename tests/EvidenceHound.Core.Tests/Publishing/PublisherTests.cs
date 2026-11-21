using System.Security.Cryptography;
using System.Text;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Publishing;

public class PublisherTests
{
    private static readonly byte[] MfaFile = Encoding.UTF8.GetBytes("{\"admins_with_mfa\": 4}");
    private static readonly byte[] BackupFile = Encoding.UTF8.GetBytes("{\"jobs_ok\": 12}");
    private static readonly Evidence Mfa = Evidence("ev-0001", "mfa-admins.json", MfaFile);
    private static readonly Evidence Backup = Evidence("ev-0002", "backup-jobs.json", BackupFile);

    private readonly InMemoryCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static IReadOnlyList<AuditUpdate> Plan() =>
    [
        new AuditUpdate.UploadEvidence(Mfa),
        new AuditUpdate.UploadEvidence(Backup),
        new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"]),
        new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-3.1", ComplianceResult.PartiallyCompliant, "Restores untested.", ["ev-0002", "ev-0001"]),
        new AuditUpdate.UpdateAppliedControl("AC-MFA", ControlState.Active),
    ];

    private Publisher Publisher(Dictionary<string, byte[]>? files = null) =>
        new(_ca, new InMemoryEvidenceFiles(files ?? new() { ["ev-0001"] = MfaFile, ["ev-0002"] = BackupFile }));

    [Fact]
    public async Task Upload_links_the_evidence_to_its_applied_controls_whether_sent_or_skipped()
    {
        var linked = new AuditUpdate.UploadEvidence(Mfa) { AppliedControlRefIds = ["AC-AUTO"] };

        var first = await Publisher().PublishAsync([linked], _ct);
        var second = await Publisher().PublishAsync([linked], _ct);

        (first.Sent, second.Skipped).ShouldBe((1, 1));
        _ca.Calls.Where(c => c.StartsWith("link", StringComparison.Ordinal)).ShouldBe(["link uuid-ev-0001 to AC-AUTO", "link uuid-ev-0001 to AC-AUTO"]);
    }

    [Fact]
    public async Task A_link_that_fails_fails_the_upload_item()
    {
        _ca.MissingControls.Add("AC-NONE");

        var report = await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa) { AppliedControlRefIds = ["AC-NONE"] }], _ct);

        report.Items.ShouldHaveSingleItem().Problems.ShouldHaveSingleItem().Code.ShouldBe("ciso_assistant.not_found");
    }

    [Fact]
    public async Task Upload_without_controls_links_nothing()
    {
        await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa)], _ct);

        _ca.Calls.ShouldNotContain(c => c.StartsWith("link", StringComparison.Ordinal));
    }

    [Fact]
    public void Dry_run_calls_nothing()
    {
        var report = Core.Publishing.Publisher.DryRun(Plan());

        _ca.Calls.ShouldBeEmpty();
        report.Planned.ShouldBe(5);
        (report.Sent, report.Skipped, report.Failed).ShouldBe((0, 0, 0));
        report.Items.Select(i => i.Outcome).ShouldAllBe(o => o == PublishOutcome.Planned);
    }

    [Fact]
    public async Task Sends_every_update_in_plan_order()
    {
        var report = await Publisher().PublishAsync(Plan(), _ct);

        report.Sent.ShouldBe(5);
        report.HasFailures.ShouldBeFalse();
        _ca.Calls.ShouldBe([
            "find-or-create ev-0001", "upload uuid-ev-0001 mfa-admins.json",
            "find-or-create ev-0002", "upload uuid-ev-0002 backup-jobs.json",
            "find EX-1.1", "update ra-EX-1.1",
            "find EX-3.1", "update ra-EX-3.1",
            "control AC-MFA",
        ]);
        _ca.Controls["AC-MFA"].ShouldBe(ControlState.Active);
    }

    [Fact]
    public async Task Requirement_updates_cite_ciso_assistant_evidence_ids_not_ref_ids()
    {
        await Publisher().PublishAsync(Plan(), _ct);

        _ca.Requirements["ra-EX-1.1"].ShouldBe(new RequirementAssessmentValues(ComplianceResult.Compliant, "All admins use MFA.", ["uuid-ev-0001"]), new RequirementValuesComparer());
        _ca.Requirements["ra-EX-3.1"].EvidenceIds.ShouldBe(["uuid-ev-0002", "uuid-ev-0001"]);
    }

    [Fact]
    public async Task Failed_item_does_not_stop_others()
    {
        _ca.MissingRequirements.Add("EX-1.1");

        var report = await Publisher().PublishAsync(Plan(), _ct);

        (report.Sent, report.Failed).ShouldBe((4, 1));
        report.HasFailures.ShouldBeTrue();
        var failed = report.Items.Single(i => i.Outcome == PublishOutcome.Failed);
        failed.Update.ShouldBeOfType<AuditUpdate.UpdateRequirementAssessment>().RequirementRefId.ShouldBe("EX-1.1");
        failed.Problems.Select(p => p.Code).ShouldBe(["ciso_assistant.not_found"]);
        _ca.Calls.ShouldContain("update ra-EX-3.1");
        _ca.Calls.ShouldContain("control AC-MFA");
    }

    [Fact]
    public async Task Same_evidence_twice_uploads_once()
    {
        await Publisher().PublishAsync(Plan(), _ct);
        _ca.Calls.Clear();

        var second = await Publisher().PublishAsync(Plan(), _ct);

        _ca.Calls.ShouldNotContain(call => call.StartsWith("upload", StringComparison.Ordinal));
        (second.Skipped, second.Sent, second.Failed).ShouldBe((2, 3, 0));
        _ca.Requirements["ra-EX-1.1"].EvidenceIds.ShouldBe(["uuid-ev-0001"]);
    }

    [Fact]
    public async Task Changed_evidence_uploads_a_new_revision()
    {
        await Publisher().PublishAsync(Plan(), _ct);
        _ca.Calls.Clear();
        var changed = Encoding.UTF8.GetBytes("{\"admins_with_mfa\": 5}");

        await new Publisher(_ca, new InMemoryEvidenceFiles(new() { ["ev-0001"] = changed, ["ev-0002"] = BackupFile }))
            .PublishAsync([new AuditUpdate.UploadEvidence(Evidence("ev-0001", "mfa-admins.json", changed))], _ct);

        _ca.Calls.ShouldBe(["find-or-create ev-0001", "upload uuid-ev-0001 mfa-admins.json"]);
    }

    [Fact]
    public async Task Evidence_whose_file_does_not_match_its_hash_is_not_uploaded()
    {
        var report = await Publisher(new() { ["ev-0001"] = Encoding.UTF8.GetBytes("tampered"), ["ev-0002"] = BackupFile })
            .PublishAsync([new AuditUpdate.UploadEvidence(Mfa)], _ct);

        report.Items.Single().Problems.Select(p => p.Code).ShouldBe(["evidence.hash_mismatch"]);
        _ca.Calls.ShouldNotContain(call => call.StartsWith("upload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Evidence_lookup_failure_fails_its_upload_and_the_requirements_citing_it()
    {
        _ca.UnreachableEvidence.Add("ev-0001");

        var report = await Publisher().PublishAsync(Plan(), _ct);

        report.Items[0].Problems.Select(p => p.Code).ShouldBe(["ciso_assistant.unavailable"]);
        report.Items[2].Problems.Select(p => p.Code).ShouldBe(["publish.evidence_unresolved"]);
        report.Items[1].Outcome.ShouldBe(PublishOutcome.Sent);
    }

    [Fact]
    public async Task Missing_evidence_file_fails_its_upload()
    {
        var report = await Publisher(new() { ["ev-0002"] = BackupFile }).PublishAsync([new AuditUpdate.UploadEvidence(Mfa)], _ct);

        report.Items.Single().Problems.Select(p => p.Code).ShouldBe(["evidence.file_not_found"]);
    }

    [Fact]
    public async Task Requirement_citing_evidence_that_failed_to_upload_fails()
    {
        var report = await Publisher(new() { ["ev-0002"] = BackupFile }).PublishAsync(Plan(), _ct);

        var failed = report.Items.Where(i => i.Outcome == PublishOutcome.Failed).ToList();
        failed.Count.ShouldBe(3);
        failed[1].Problems.Select(p => p.Code).ShouldBe(["publish.evidence_unresolved"]);
        failed[1].Problems[0].Message.ShouldContain("ev-0001");
        _ca.Requirements.ShouldBeEmpty();
        _ca.Controls.ShouldContainKey("AC-MFA");
    }

    [Fact]
    public async Task Every_item_describes_its_update()
    {
        var report = await Publisher().PublishAsync(Plan(), _ct);

        report.Items.Select(i => i.Description).ShouldBe([
            "upload evidence ev-0001 (mfa-admins.json)",
            "upload evidence ev-0002 (backup-jobs.json)",
            "set requirement EX-1.1 in EXAMPLE-BASELINE-2026 to compliant",
            "set requirement EX-3.1 in EXAMPLE-BASELINE-2026 to partially_compliant",
            "set applied control AC-MFA to active",
        ]);
    }

    private static Evidence Evidence(string id, string fileName, byte[] content) =>
        Core.Checks.Evidence.Create(id, fileName, "application/json", Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, CheckTestData.CollectedAt).Value();

    private sealed class RequirementValuesComparer : IEqualityComparer<RequirementAssessmentValues>
    {
        public bool Equals(RequirementAssessmentValues? x, RequirementAssessmentValues? y) =>
            x is not null && y is not null && x.Result == y.Result && x.Observation == y.Observation && x.EvidenceIds.SequenceEqual(y.EvidenceIds);

        public int GetHashCode(RequirementAssessmentValues obj) => obj.Result.GetHashCode();
    }
}
