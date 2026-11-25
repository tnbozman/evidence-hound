using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s10-04: each evidence plan row is an evidence item in CISO Assistant, and each row a person provides is also a recurring task that
/// reminds the owner with the runbook. Occurrences, their status and the evidence itself stay CISO Assistant's.
/// </summary>
public class TaskTemplatePlanTests
{
    private static readonly string Runbook = "# Admin review\n\nExport the admin list and send it to the GRC team.\n";

    [Fact]
    public void Interval_becomes_recurring_schedule()
    {
        var declared = Declared();

        var task = declared.Single(d => d.Kind == ScopeKind.TaskTemplate);
        task.Key.ShouldBe("EV-ADMIN-REVIEW");
        task.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "EV-ADMIN-REVIEW: Multi-factor authentication",
            // Without the trailing line break CISO Assistant drops when it reads a description back, so an unchanged runbook compares equal.
            ["description"] = Runbook.TrimEnd(),
            ["domain"] = "Example Pty Ltd",
            ["assignees"] = "Example 365 owners, GRC team",
            ["schedule"] = "every 3 months",
            ["evidence"] = "EV-ADMIN-REVIEW",
            ["controls"] = "AC-MFA",
            ["requirements"] = "EXAMPLE-BASELINE-2026/EX-1.2",
            ["enabled"] = "true",
        });
        task.Initial["first_due"].ShouldBe("2027-02-23");

        // A row naming no requirements links every requirement its control supports in each audit that lists the control.
        var mfa = declared.Single(d => d.Kind == ScopeKind.Evidence && d.Key == "EV-MFA");
        mfa.Fields.ContainsKey("requirements").ShouldBeFalse();
        TaskSchedules.Of(new EvidenceInterval(3, IntervalUnit.Month)).ShouldBe(new TaskSchedule("MONTHLY", 3));
        TaskSchedules.Of(new EvidenceInterval(6, IntervalUnit.Month)).ShouldBe(new TaskSchedule("MONTHLY", 6));
        TaskSchedules.Of(new EvidenceInterval(12, IntervalUnit.Month)).ShouldBe(new TaskSchedule("YEARLY", 1));
    }

    [Fact]
    public void A_weekly_row_is_due_a_week_from_today()
    {
        var task = Declared(EvidencePlanScopeTests.Plan.Replace("every: 3 months", "every: 1 week", StringComparison.Ordinal))
            .Single(d => d.Kind == ScopeKind.TaskTemplate);

        (task.Fields["schedule"], task.Initial["first_due"]).ShouldBe(("every 1 week", "2026-11-30"));
    }

    [Fact]
    public void First_due_anchors_a_yearly_row_to_its_date_and_rolls_past_dates_forward()
    {
        // s17-06: the yearly CMMC affirmation is due before the anniversary of the last assessment, not a year from whenever it is applied.
        var plan = EvidencePlanScopeTests.Plan.Replace("every: 3 months", "every: 12 months\n    first_due: \"2026-12-15\"", StringComparison.Ordinal);
        Declared(plan).Single(d => d.Kind == ScopeKind.TaskTemplate).Initial["first_due"].ShouldBe("2026-12-15");

        var past = EvidencePlanScopeTests.Plan.Replace("every: 3 months", "every: 12 months\n    first_due: \"2025-12-15\"", StringComparison.Ordinal);
        Declared(past).Single(d => d.Kind == ScopeKind.TaskTemplate).Initial["first_due"].ShouldBe("2026-12-15");
    }

    [Fact]
    public void Every_row_is_an_evidence_item_labelled_with_its_method_and_collector()
    {
        var items = Declared().Where(d => d.Kind == ScopeKind.Evidence).ToDictionary(d => d.Key, d => d.Fields);

        // CISO Assistant takes a label only as letters, digits, underscores and hyphens, at most 36 (the s10-04 trial's 400s): the method,
        // and the collector's name when it fits.
        items["EV-ADMIN-REVIEW"]["labels"].ShouldBe("manual");
        items["EV-ADMIN-REVIEW"]["owner"].ShouldBe("Example 365 owners");
        items["EV-MFA"]["labels"].ShouldBe("automated, example-m365");
        items["EV-MFA"]["controls"].ShouldBe("AC-MFA");
        items["EV-MFA"].ContainsKey("owner").ShouldBeFalse();
    }

    [Fact]
    public void A_collector_name_that_is_no_label_is_left_out()
    {
        var plan = EvidencePlanScopeTests.Plan.Replace("collector: example-m365", "collector: example-m365-identity-and-device-baseline", StringComparison.Ordinal);

        Declared(plan).Single(d => d.Kind == ScopeKind.Evidence && d.Key == "EV-MFA").Fields["labels"].ShouldBe("automated");
    }

    [Fact]
    public void Occurrence_status_is_never_in_the_plan()
    {
        var declared = Declared().Where(d => d.Kind is ScopeKind.TaskTemplate or ScopeKind.Evidence).ToArray();
        var held = declared.Select(d => new ActualObject(d, ScopeMarker.State(d.Kind, d.Fields))).ToArray();

        // Once applied, the next plan is empty whatever the occurrences hold: no field hound compares is an occurrence's, an evidence
        // revision or a review, and the state reader reads only these fields (TaskTemplateClientTests).
        ScopePlanner.Plan(declared, held, prune: true).IsEmpty.ShouldBeTrue();
        declared.SelectMany(d => d.Fields.Keys.Concat(d.Initial.Keys)).Distinct().Order(StringComparer.Ordinal).ShouldBe(
            ["assignees", "controls", "description", "domain", "enabled", "evidence", "first_due", "labels", "name", "owner", "requirements", "schedule"]);
    }

    [Fact]
    public void An_owner_contacts_email_is_part_of_its_team()
    {
        Declared().Single(d => d.Kind == ScopeKind.Team && d.Key == "Example 365 owners").Fields["email"].ShouldBe("owners-365@example.com");
    }

    private static IReadOnlyList<ScopeObject> Declared(string plan = EvidencePlanScopeTests.Plan)
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            new ScopeSource("scope/access.yaml", EvidencePlanScopeTests.Access),
            evidencePlan: new ScopeSource(EvidencePlanScopeTests.PlanPath, plan));
        parsed.Problems.ShouldBeEmpty();
        var inputs = ScopeInputs.None with
        {
            Today = new DateOnly(2026, 11, 23),
            Runbooks = new Dictionary<string, string>(StringComparer.Ordinal) { ["manual-sources/admin-review.md"] = Runbook },
        };
        return ScopeDesired.From(parsed.Scope, inputs);
    }
}
