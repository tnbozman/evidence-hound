using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s06-10: an audit with a cycle is a series of period audits, opened, moved to review and locked by planning on the day's date.</summary>
public sealed class RollTests
{
    private static readonly SourceLine At = new("scope/audits/ex-e8-backup.yaml", 1);
    private static readonly string E8 = "urn:intuitem:risk:framework:essential-eight-2023";

    [Fact]
    public void New_period_opens_from_previous_as_baseline()
    {
        var scope = Scope(Series());
        var held = Apply(scope, new DateOnly(2026, 9, 30), []);

        var plan = ScopePlanner.Plan(ScopeDesired.From(scope, Inputs(new DateOnly(2026, 10, 1))), held, prune: false);

        var opened = plan.Changes.Single(c => c.Kind == ScopeKind.Audit && c.Action == ScopeAction.Create);
        opened.Key.ShouldBe("EX-E8-BACKUP-2026Q4");
        opened.Desired!.Initial["baseline"].ShouldBe("EX-E8-BACKUP-2026Q3");
        (opened.Desired.Fields["status"], opened.Desired.Fields["name"], opened.Desired.Fields["eta"]).ShouldBe(("in_progress", "Backups 2026Q4", "2026-12-31"));
        plan.Changes.Where(c => c.Kind == ScopeKind.Assessment).ShouldAllBe(c => c.Key.StartsWith("EX-E8-BACKUP-2026Q4/", StringComparison.Ordinal));
        plan.Changes.Where(c => c.Kind == ScopeKind.Assessment).ShouldNotBeEmpty();
    }

    [Fact]
    public void Ended_period_moves_to_in_review_and_is_not_written_again()
    {
        var scope = Scope(Series());
        var held = Apply(scope, new DateOnly(2026, 9, 30), []);

        var plan = ScopePlanner.Plan(ScopeDesired.From(scope, Inputs(new DateOnly(2026, 10, 1))), held, prune: false);

        plan.Changes.Single(c => c.Key == "EX-E8-BACKUP-2026Q3").Fields.ShouldBe([new FieldChange("status", "in_progress", "in_review")]);
        var later = Apply(scope, new DateOnly(2026, 10, 1), held);
        ScopePlanner.Plan(ScopeDesired.From(scope, Inputs(new DateOnly(2026, 11, 21))), later, prune: false).IsEmpty.ShouldBeTrue();
        ScopeDesired.From(scope, Inputs(new DateOnly(2026, 11, 21))).ShouldNotContain(o => o.Key.StartsWith("EX-E8-BACKUP-2026Q3/", StringComparison.Ordinal));
    }

    [Fact]
    public void Sign_off_in_file_locks_the_period()
    {
        var held = Apply(Scope(Series()), new DateOnly(2026, 10, 1), Apply(Scope(Series()), new DateOnly(2026, 9, 30), []));
        var signed = Scope(Series() with { SignOffs = [new SignOff("2026Q3", "ceo@example.com", "2026-10-14")] });

        var plan = ScopePlanner.Plan(ScopeDesired.From(signed, Inputs(new DateOnly(2026, 10, 15))), held, prune: false);

        plan.Changes.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Key.ShouldBe("EX-E8-BACKUP-2026Q3"),
            c => c.Fields.ShouldBe([new FieldChange("status", "in_review", "done"), new FieldChange("locked", null, "true")], ignoreOrder: true));
        var locked = Apply(signed, new DateOnly(2026, 10, 15), held);
        locked.Single(a => a.Held.Key == "EX-E8-BACKUP-2026Q3").Locked.ShouldBeTrue();
        ScopePlanner.Plan(ScopeDesired.From(signed, Inputs(new DateOnly(2026, 10, 16))), locked, prune: false).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Auto_close_locks_a_period_when_it_ends()
    {
        var auto = Series() with { Cycle = new AuditCycle("quarter", "2026-07-01", At) { Close = "auto" } };
        var held = Apply(Scope(auto), new DateOnly(2026, 9, 30), []);

        var plan = ScopePlanner.Plan(ScopeDesired.From(Scope(auto), Inputs(new DateOnly(2026, 10, 1))), held, prune: false);

        plan.Changes.Single(c => c.Key == "EX-E8-BACKUP-2026Q3").Fields.ShouldBe([new FieldChange("status", "in_progress", "done"), new FieldChange("locked", null, "true")], ignoreOrder: true);
    }

    [Fact]
    public void Missed_boundary_is_caught_up_once()
    {
        var scope = Scope(Series() with { Cycle = new AuditCycle("quarter", "2026-04-01", At) });
        var held = Apply(scope, new DateOnly(2026, 6, 30), []);

        var plan = ScopePlanner.Plan(ScopeDesired.From(scope, Inputs(new DateOnly(2026, 11, 21))), held, prune: false);

        plan.Changes.Where(c => c.Kind == ScopeKind.Audit).Select(c => (c.Action, c.Key, c.Desired!.Fields["status"], c.Desired.Initial.GetValueOrDefault("baseline"))).ShouldBe(
        [
            (ScopeAction.Update, "EX-E8-BACKUP-2026Q2", "in_review", null),
            (ScopeAction.Create, "EX-E8-BACKUP-2026Q3", "in_review", "EX-E8-BACKUP-2026Q2"),
            (ScopeAction.Create, "EX-E8-BACKUP-2026Q4", "in_progress", "EX-E8-BACKUP-2026Q3"),
        ]);
        var caughtUp = Apply(scope, new DateOnly(2026, 11, 21), held);
        ScopePlanner.Plan(ScopeDesired.From(scope, Inputs(new DateOnly(2026, 11, 22))), caughtUp, prune: false).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Deployment_target_naming_the_series_computes_rows_of_the_open_period()
    {
        var target = new ScopeTarget("EX-E8-BACKUP", [RequirementKey.Ref("ISM-1511")], [], At);

        var rows = ScopeDesired.From(Scope(Series()), Inputs(new DateOnly(2026, 11, 21)) with { Targets = [target] })
            .Where(o => o.Kind == ScopeKind.Assessment).Select(o => o.Key);

        rows.ShouldBe(["EX-E8-BACKUP-2026Q4/ISM-1515"]);
    }

    [Fact]
    public void Plan_lists_period_openings_closings_and_pending_sign_offs()
    {
        var scope = Scope(Series() with { Cycle = new AuditCycle("quarter", "2026-04-01", At) });
        var held = Apply(scope, new DateOnly(2026, 9, 30), []);
        var declared = ScopeDesired.From(scope, Inputs(new DateOnly(2026, 10, 1)));

        var notes = ScopePeriods.Notes(declared, ScopePlanner.Plan(declared, held, prune: false));

        notes.ShouldBe(
        [
            new PeriodNote("EX-E8-BACKUP-2026Q2", PeriodEvent.AwaitingSignOff, null),
            new PeriodNote("EX-E8-BACKUP-2026Q3", PeriodEvent.Closes, null),
            new PeriodNote("EX-E8-BACKUP-2026Q4", PeriodEvent.Opens, "EX-E8-BACKUP-2026Q3"),
        ]);
    }

    [Fact]
    public void Plan_lists_a_period_locked_by_its_sign_off()
    {
        var held = Apply(Scope(Series()), new DateOnly(2026, 10, 1), Apply(Scope(Series()), new DateOnly(2026, 9, 30), []));
        var signed = Scope(Series() with { SignOffs = [new SignOff("2026Q3", "ceo@example.com", "2026-10-14")] });
        var declared = ScopeDesired.From(signed, Inputs(new DateOnly(2026, 10, 15)));

        ScopePeriods.Notes(declared, ScopePlanner.Plan(declared, held, prune: false)).ShouldBe([new PeriodNote("EX-E8-BACKUP-2026Q3", PeriodEvent.SignedOff, null)]);
    }

    /// <summary>What CISO Assistant holds after applying the scope on <paramref name="today"/> over what it held: every declared object, as declared.</summary>
    private static ActualObject[] Apply(ScopeDocument scope, DateOnly today, ActualObject[] held)
    {
        var declared = ScopeDesired.From(scope, Inputs(today));
        var plan = ScopePlanner.Plan(declared, held, prune: false);
        var written = plan.Changes.ToDictionary(c => (c.Kind, c.Key), c => c.Desired!);
        return
        [
            .. held.Where(h => !written.ContainsKey((h.Held.Kind, h.Held.Key))),
            .. written.Values.Select(o => new ActualObject(o with { Initial = new Dictionary<string, string>() }, ScopeMarker.State(o.Kind, o.Fields))
            {
                Locked = o.Fields.GetValueOrDefault("locked") == "true",
            }),
        ];
    }

    /// <summary>A quarterly backup audit from 2026-07-01, closed by sign-off, with one hand-written result.</summary>
    private static ScopeAudit Series() =>
        new("EX-E8-BACKUP", "Backups", Ref(E8), [Ref("1"), Ref("2")], Ref("Example Pty Ltd"), Ref("Corporate IT"), At)
        {
            Cycle = new AuditCycle("quarter", "2026-07-01", At),
            Assessments = [new HandAssessment(new RequirementRef(RequirementKey.Ref("ISM-1515"), At), "done", ComplianceResult.Compliant, "Restore tested.")],
            Unlisted = new UnlistedRule(ComplianceResult.NotAssessed, null, At),
        };

    private static ScopeDocument Scope(ScopeAudit audit) => new(
        new ScopeSetup("Global", [new ScopeFramework(E8, "urn:intuitem:risk:library:essential-eight-2023", null, null, At)], []),
        [],
        [audit]);

    /// <summary>A framework of two ML2 requirements; no targets, so <c>unlisted</c> is not applied.</summary>
    private static ScopeInputs Inputs(DateOnly today) => new(
        new Dictionary<string, FrameworkSnapshot>
        {
            [E8] = new("urn:intuitem:risk:library:essential-eight-2023", E8, 3, [new("1", "ML1"), new("2", "ML2")],
            [
                new($"{E8}:ism-1511", "ISM-1511", null, ["1", "2"], "Backups"),
                new($"{E8}:ism-1515", "ISM-1515", null, ["1", "2"], "Restore tests"),
            ]),
        },
        null,
        new Dictionary<string, IReadOnlyList<ScopeFile>>())
    {
        Today = today,
    };

    private static ScopeRef Ref(string value) => new(value, At);
}
