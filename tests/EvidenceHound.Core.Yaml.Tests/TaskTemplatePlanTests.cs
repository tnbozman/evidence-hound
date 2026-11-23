using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s10-04: each evidence plan row is an evidence item in CISO Assistant, and each row a person provides is also a recurring task that
/// reminds the owner with the runbook. Occurrences, their status and the evidence itself stay CISO Assistant's.
/// </summary>
public class TaskTemplatePlanTests
{
    private const string Runbook = "# Admin review\n\nExport the admin list and send it to the GRC team.\n";

    [Fact]
    public void Interval_becomes_recurring_schedule()
    {
        var declared = Declared();

        var task = declared.Single(d => d.Kind == ScopeKind.TaskTemplate);
        task.Key.ShouldBe("EV-ADMIN-REVIEW");
        task.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "EV-ADMIN-REVIEW: Multi-factor authentication",
            ["description"] = Runbook,
            ["domain"] = "Example Pty Ltd",
            ["assignees"] = "Example 365 owners, GRC team",
            ["schedule"] = "every 3 months",
            ["evidence"] = "EV-ADMIN-REVIEW",
            ["controls"] = "AC-MFA",
            ["requirements"] = "EX-1.2",
            ["enabled"] = "true",
        });
        task.Initial["first_due"].ShouldBe("2027-02-23");
        TaskSchedules.Of(3).ShouldBe(new TaskSchedule("MONTHLY", 3));
        TaskSchedules.Of(6).ShouldBe(new TaskSchedule("MONTHLY", 6));
        TaskSchedules.Of(12).ShouldBe(new TaskSchedule("YEARLY", 1));
    }

    [Fact]
    public void Every_row_is_an_evidence_item_labelled_with_its_method_and_collector()
    {
        var items = Declared().Where(d => d.Kind == ScopeKind.Evidence).ToDictionary(d => d.Key, d => d.Fields);

        items["EV-ADMIN-REVIEW"]["labels"].ShouldBe("method:manual");
        items["EV-ADMIN-REVIEW"]["owner"].ShouldBe("Example 365 owners");
        items["EV-MFA"]["labels"].ShouldBe("collector:example-m365, method:automated");
        items["EV-MFA"]["controls"].ShouldBe("AC-MFA");
        items["EV-MFA"].ContainsKey("owner").ShouldBeFalse();
    }

    [Fact]
    public void Occurrence_status_is_never_in_the_plan()
    {
        var declared = Declared().Where(d => d.Kind is ScopeKind.TaskTemplate or ScopeKind.Evidence).ToArray();
        var held = declared.Select(d => new ActualObject(
            d with { Fields = new Dictionary<string, string>(d.Fields) { ["status"] = "pending", ["next_occurrence"] = "2027-02-23", ["attachment"] = "export.csv" } },
            "applied")).ToArray();

        var plan = ScopePlanner.Plan(declared, held, prune: true);

        plan.IsEmpty.ShouldBeTrue();
        declared.SelectMany(d => d.Fields.Keys).ShouldNotContain("status");
    }

    [Fact]
    public void An_owner_contacts_email_is_part_of_its_team()
    {
        Declared().Single(d => d.Kind == ScopeKind.Team && d.Key == "Example 365 owners").Fields["email"].ShouldBe("owners-365@example.com");
    }

    private static IReadOnlyList<ScopeObject> Declared()
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            new ScopeSource("scope/access.yaml", EvidencePlanScopeTests.Access),
            evidencePlan: new ScopeSource(EvidencePlanScopeTests.PlanPath, EvidencePlanScopeTests.Plan));
        parsed.Problems.ShouldBeEmpty();
        var inputs = ScopeInputs.None with
        {
            Today = new DateOnly(2026, 11, 23),
            Runbooks = new Dictionary<string, string>(StringComparer.Ordinal) { ["manual-sources/admin-review.md"] = Runbook },
        };
        return ScopeDesired.From(parsed.Scope, inputs);
    }
}
