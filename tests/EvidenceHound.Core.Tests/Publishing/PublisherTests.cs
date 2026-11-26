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
    public async Task Upload_with_a_known_id_sends_a_revision_without_searching()
    {
        // s18-D3: one search per evidence item in a run, not two.
        await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa)], _ct);
        _ca.Calls.Clear();

        var report = await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa) { KnownId = "uuid-ev-0001" }, new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"])], _ct);

        report.HasFailures.ShouldBeFalse();
        _ca.Calls.ShouldNotContain(c => c.StartsWith("find-or-create", StringComparison.Ordinal));
        _ca.Calls.ShouldContain(c => c.StartsWith("upload uuid-ev-0001", StringComparison.Ordinal));
        _ca.Requirements.Values.ShouldContain(v => v.EvidenceIds.Contains("uuid-ev-0001"));
    }

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
    public async Task Evidence_is_created_with_a_name_unique_in_its_folder()
    {
        await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa)], _ct);

        _ca.EvidenceNames["ev-0001"].ShouldBe("mfa-admins.json (ev-0001)");
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
            "audit EXAMPLE-BASELINE-2026", "find EX-1.1", "update ra-EX-1.1",
            "find EX-3.1", "update ra-EX-3.1",
            "control AC-MFA",
        ]);
        _ca.Controls["AC-MFA"].ShouldBe(ControlState.Active);
    }

    [Fact]
    public async Task Update_with_row_ids_writes_those_rows_without_looking_them_up()
    {
        var update = new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"])
        {
            RowIds = ["ra-a", "ra-b"],
            KnownEvidenceIds = new Dictionary<string, string> { ["ev-0001"] = "uuid-known" },
        };

        var report = await Publisher().PublishAsync([update], _ct);

        report.Sent.ShouldBe(1);
        _ca.Calls.ShouldBe(["update ra-a", "update ra-b"]);
        _ca.Requirements["ra-b"].EvidenceIds.ShouldBe(["uuid-known"]);
    }

    [Fact]
    public async Task Row_update_keeps_the_scope_documents_on_each_row()
    {
        var update = new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"])
        {
            RowIds = ["ra-a", "ra-b"],
            KnownEvidenceIds = new Dictionary<string, string> { ["ev-0001"] = "uuid-known" },
            KeptEvidenceIds = new Dictionary<string, IReadOnlyList<string>> { ["ra-b"] = ["uuid-policy"] },
        };

        await Publisher().PublishAsync([update], _ct);

        _ca.Requirements["ra-a"].EvidenceIds.ShouldBe(["uuid-known"]);
        _ca.Requirements["ra-b"].EvidenceIds.ShouldBe(["uuid-known", "uuid-policy"]);
    }

    [Fact]
    public async Task Evidence_uploaded_in_the_plan_wins_over_a_known_id()
    {
        var update = new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"])
        {
            RowIds = ["ra-a"],
            KnownEvidenceIds = new Dictionary<string, string> { ["ev-0001"] = "uuid-stale" },
        };

        await Publisher().PublishAsync([new AuditUpdate.UploadEvidence(Mfa), update], _ct);

        _ca.Requirements["ra-a"].EvidenceIds.ShouldBe(["uuid-ev-0001"]);
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

    [Fact]
    public async Task Rows_all_writes_one_result_to_every_matching_row()
    {
        _ca.Rows.AddRange([Row("ra-bnd", "bnd", "ACT-01", "1"), Row("ra-idn", "idn", "ACT-01", "1")]);

        var report = await Publisher().PublishAsync([Requirement(RequirementKey.Ref("ACT-01", allRows: true))], _ct);

        report.Items.ShouldHaveSingleItem().Outcome.ShouldBe(PublishOutcome.Sent);
        _ca.Requirements.Keys.Order(StringComparer.Ordinal).ShouldBe(["ra-bnd", "ra-idn"]);
    }

    [Fact]
    public async Task Ref_matching_several_rows_without_rows_all_fails_listing_their_urns()
    {
        _ca.Rows.AddRange([Row("ra-bnd", "bnd", "ACT-01", "1"), Row("ra-idn", "idn", "ACT-01", "1")]);

        var report = await Publisher().PublishAsync([Requirement(RequirementKey.Ref("ACT-01"))], _ct);

        var problem = report.Items.ShouldHaveSingleItem().Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("mapping.ref_ambiguous");
        problem.Message.ShouldContain("urn:example:req:bnd:ACT-01");
        problem.Message.ShouldContain("urn:example:req:idn:ACT-01");
        _ca.Requirements.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requirement_named_by_urn_writes_only_that_row()
    {
        _ca.Rows.AddRange([Row("ra-bnd", "bnd", "ACT-01", "1"), Row("ra-idn", "idn", "ACT-01", "1")]);

        await Publisher().PublishAsync([Requirement(RequirementKey.Urn("urn:example:req:idn:ACT-01"))], _ct);

        _ca.Requirements.Keys.ShouldBe(["ra-idn"]);
    }

    [Fact]
    public async Task Requirements_outside_selected_groups_are_skipped()
    {
        _ca.SelectedGroups.AddRange(["1", "2"]);
        _ca.Rows.AddRange([Row("ra-bnd", "bnd", "ACT-01", "1"), Row("ra-ml3", "idn", "ACT-03", "3"), Row("ra-none", "idn", "ACT-04")]);

        var report = await Publisher().PublishAsync(
            [Requirement(RequirementKey.Ref("ACT-01")), Requirement(RequirementKey.Ref("ACT-03")), Requirement(RequirementKey.Ref("ACT-04"))], _ct);

        report.Items.Select(i => (i.Outcome, i.Note)).ShouldBe([
            (PublishOutcome.Sent, null),
            (PublishOutcome.Skipped, "outside selected groups"),
            (PublishOutcome.Skipped, "outside selected groups"),
        ]);
        report.Failed.ShouldBe(0);
        _ca.Requirements.Keys.ShouldBe(["ra-bnd"]);
    }

    [Fact]
    public async Task Rows_all_writes_only_the_rows_in_selected_groups()
    {
        _ca.SelectedGroups.Add("1");
        _ca.Rows.AddRange([Row("ra-bnd", "bnd", "ACT-01", "1"), Row("ra-idn", "idn", "ACT-01", "2")]);

        var report = await Publisher().PublishAsync([Requirement(RequirementKey.Ref("ACT-01", allRows: true))], _ct);

        report.Sent.ShouldBe(1);
        _ca.Requirements.Keys.ShouldBe(["ra-bnd"]);
    }

    [Fact]
    public async Task Audit_groups_are_read_once_per_audit()
    {
        await Publisher().PublishAsync([Requirement(RequirementKey.Ref("EX-1.1")), Requirement(RequirementKey.Ref("EX-1.2"))], _ct);

        _ca.Calls.Count(c => c.StartsWith("audit", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Failure_in_one_audit_does_not_stop_the_next()
    {
        _ca.MissingAudits.Add("EX-A");

        var report = await Publisher().PublishAsync(
        [
            new AuditUpdate.UpdateRequirementAssessment("EX-A", "R-1", ComplianceResult.Compliant, "Checked.", []),
            new AuditUpdate.UpdateRequirementAssessment("EX-A", "R-2", ComplianceResult.Compliant, "Checked.", []),
            new AuditUpdate.UpdateRequirementAssessment("EX-B", "R-1", ComplianceResult.Compliant, "Checked.", []),
        ], _ct);

        report.Items.Select(i => i.Outcome).ShouldBe([PublishOutcome.Failed, PublishOutcome.Failed, PublishOutcome.Sent]);
        report.Audits.Select(a => (a.ComplianceAssessmentRefId, a.Sent, a.Failed)).ShouldBe([("EX-A", 0, 2), ("EX-B", 1, 0)]);
    }

    private static RemoteRequirementAssessment Row(string id, string domain, string refId, params string[] groups) =>
        new(id, $"urn:example:req:{domain}:{refId}", refId, groups);

    private static AuditUpdate.UpdateRequirementAssessment Requirement(RequirementKey key) =>
        new("EXAMPLE-2026", key.Value, ComplianceResult.Compliant, "Checked.", []) { Requirement = key };

    private static Evidence Evidence(string id, string fileName, byte[] content) =>
        Core.Checks.Evidence.Create(id, fileName, "application/json", Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, CheckTestData.CollectedAt).Value();

    private sealed class RequirementValuesComparer : IEqualityComparer<RequirementAssessmentValues>
    {
        public bool Equals(RequirementAssessmentValues? x, RequirementAssessmentValues? y) =>
            x is not null && y is not null && x.Result == y.Result && x.Observation == y.Observation && x.EvidenceIds.SequenceEqual(y.EvidenceIds);

        public int GetHashCode(RequirementAssessmentValues obj) => obj.Result.GetHashCode();
    }
}
