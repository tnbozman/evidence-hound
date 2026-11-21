using EvidenceHound.Core.Scope;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Scope;

public class ScopePlanTests
{
    [Fact]
    public void New_domain_is_a_create()
    {
        var domain = Declared(ScopeKind.Domain, "Example Pty Ltd", ("description", "The whole company."));

        var plan = ScopePlanner.Plan([domain], [], prune: false);

        var change = plan.Changes.ShouldHaveSingleItem();
        (change.Kind, change.Key, change.Action).ShouldBe((ScopeKind.Domain, "Example Pty Ltd", ScopeAction.Create));
        change.Fields.ShouldBe([new FieldChange("description", null, "The whole company.")]);
        change.State.ShouldBe(ScopeMarker.Fingerprint(domain.Fields));
        plan.Drift.ShouldBeEmpty();
    }

    [Fact]
    public void Changed_owner_is_a_field_update()
    {
        var before = Declared(ScopeKind.System, "Example laptops", ("type", "supporting"), ("owner", "it@example.com"));
        var after = Declared(ScopeKind.System, "Example laptops", ("type", "supporting"), ("owner", "desktop@example.com"));

        var plan = ScopePlanner.Plan([after], [Applied(before)], prune: false);

        var change = plan.Changes.ShouldHaveSingleItem();
        (change.Kind, change.Key, change.Action).ShouldBe((ScopeKind.System, "Example laptops", ScopeAction.Update));
        change.Fields.ShouldBe([new FieldChange("owner", "it@example.com", "desktop@example.com")]);
        change.State.ShouldBe(ScopeMarker.Fingerprint(after.Fields));
        plan.Drift.ShouldBeEmpty();
    }

    [Fact]
    public void Object_edited_in_ui_is_reported_as_drift()
    {
        var declared = Declared(ScopeKind.Control, "AC-MFA", ("name", "Multi-factor authentication"), ("priority", "1"));
        var edited = new ActualObject(Declared(ScopeKind.Control, "AC-MFA", ("name", "MFA everywhere"), ("priority", "1")), ScopeMarker.Fingerprint(declared.Fields));

        var plan = ScopePlanner.Plan([declared], [edited], prune: false);

        var drift = plan.Drift.ShouldHaveSingleItem();
        drift.ShouldBe(new ScopeDrift(ScopeKind.Control, "AC-MFA", DriftReason.Edited, Managed: true, [new FieldChange("name", "MFA everywhere", "Multi-factor authentication")]), new DriftComparer());
        var change = plan.Changes.ShouldHaveSingleItem();
        (change.Action, change.Fields.ShouldHaveSingleItem().New).ShouldBe((ScopeAction.Update, "Multi-factor authentication"));
    }

    [Fact]
    public void Prune_never_deletes_unmanaged_objects()
    {
        var byHand = new ActualObject(Declared(ScopeKind.Perimeter, "Example Pty Ltd/Example baseline", ("name", "Example baseline")), AppliedState: null);
        var removed = Applied(Declared(ScopeKind.System, "Example tablets", ("type", "supporting")));

        var plan = ScopePlanner.Plan([], [byHand, removed], prune: true);

        plan.Changes.ShouldHaveSingleItem().ShouldBe(new ScopeChange(ScopeKind.System, "Example tablets", ScopeAction.Delete, [], null), new ChangeComparer());
        plan.Drift.Select(d => (d.Key, d.Reason, d.Managed)).ShouldBe(
            [("Example Pty Ltd/Example baseline", DriftReason.NotInScope, false), ("Example tablets", DriftReason.NotInScope, true)], ignoreOrder: true);
    }

    [Fact]
    public void Without_prune_nothing_is_deleted()
    {
        var removed = Applied(Declared(ScopeKind.System, "Example tablets", ("type", "supporting")));

        var plan = ScopePlanner.Plan([], [removed], prune: false);

        plan.Changes.ShouldBeEmpty();
        plan.Drift.ShouldHaveSingleItem().Reason.ShouldBe(DriftReason.NotInScope);
    }

    [Fact]
    public void Changes_are_ordered_by_dependency_and_deletes_come_last_in_reverse()
    {
        ScopeObject[] declared =
        [
            Declared(ScopeKind.Assessment, "EX-E8-2026/ISM-1486", ("result", "compliant")),
            Declared(ScopeKind.Control, "AC-MFA", ("name", "MFA")),
            Declared(ScopeKind.Document, "identity-policy", ("name", "Identity policy")),
            Declared(ScopeKind.Audit, "EX-E8-2026", ("name", "E8")),
            Declared(ScopeKind.System, "Example 365", ("type", "primary")),
            Declared(ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT", ("name", "Corporate IT")),
            Declared(ScopeKind.Domain, "Example Pty Ltd"),
            Declared(ScopeKind.Framework, "urn:example:risk:framework:example-baseline"),
        ];
        ActualObject[] gone = [Applied(Declared(ScopeKind.Domain, "Old domain")), Applied(Declared(ScopeKind.Control, "AC-OLD", ("name", "Old")))];

        var plan = ScopePlanner.Plan(declared, gone, prune: true);

        plan.Changes.Select(c => (c.Kind, c.Action)).ShouldBe(
        [
            (ScopeKind.Framework, ScopeAction.Create), (ScopeKind.Domain, ScopeAction.Create), (ScopeKind.Perimeter, ScopeAction.Create),
            (ScopeKind.System, ScopeAction.Create), (ScopeKind.Audit, ScopeAction.Create), (ScopeKind.Document, ScopeAction.Create),
            (ScopeKind.Control, ScopeAction.Create), (ScopeKind.Assessment, ScopeAction.Create),
            (ScopeKind.Control, ScopeAction.Delete), (ScopeKind.Domain, ScopeAction.Delete),
        ]);
    }

    [Fact]
    public void Unmanaged_object_that_matches_the_scope_is_marked_as_managed()
    {
        var declared = Declared(ScopeKind.Domain, "Example Pty Ltd");

        var plan = ScopePlanner.Plan([declared], [new ActualObject(declared, AppliedState: null)], prune: false);

        plan.Changes.ShouldHaveSingleItem().ShouldBe(new ScopeChange(ScopeKind.Domain, "Example Pty Ltd", ScopeAction.Update, [], ScopeMarker.Fingerprint(declared.Fields)), new ChangeComparer());
        plan.Drift.ShouldBeEmpty();
    }

    [Fact]
    public void Initial_fields_are_written_on_create_only()
    {
        var declared = Declared(ScopeKind.Control, "AC-MFA", ("name", "MFA")) with { Initial = new Dictionary<string, string> { ["status"] = "active" } };

        var create = ScopePlanner.Plan([declared], [], prune: false).Changes.ShouldHaveSingleItem();
        var later = ScopePlanner.Plan([declared], [Applied(Declared(ScopeKind.Control, "AC-MFA", ("name", "MFA")))], prune: false);

        create.Fields.ShouldBe([new FieldChange("name", null, "MFA"), new FieldChange("status", null, "active")]);
        later.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Frameworks_and_assessments_carry_no_marker()
    {
        ScopeObject[] declared = [Declared(ScopeKind.Framework, "urn:example:risk:framework:example-baseline"), Declared(ScopeKind.Assessment, "EX-E8-2026/ISM-1486", ("result", "compliant"))];
        ActualObject[] actual = [.. declared.Select(d => new ActualObject(d, AppliedState: null))];

        var plan = ScopePlanner.Plan(declared, actual, prune: false);

        plan.IsEmpty.ShouldBeTrue();
        ScopePlanner.Plan(declared, [], prune: false).Changes.ShouldAllBe(c => c.State == null);
    }

    [Fact]
    public void Evidence_the_scope_does_not_declare_is_not_drift_unless_managed()
    {
        var collected = new ActualObject(Declared(ScopeKind.Document, "example-m365/mfa-users.json", ("name", "mfa-users.json")), AppliedState: null);

        ScopePlanner.Plan([], [collected], prune: true).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Ref_id_that_ciso_assistant_filled_in_is_not_a_change()
    {
        var declared = Declared(ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT", ("name", "Corporate IT"), ("domain", "Example Pty Ltd"));
        var held = new ActualObject(
            Declared(ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT", ("name", "Corporate IT"), ("domain", "Example Pty Ltd"), ("ref_id", "PER-0001")),
            ScopeMarker.Fingerprint(declared.Fields));

        ScopePlanner.Plan([declared], [held], prune: false).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Marker_round_trips_through_the_description()
    {
        var fingerprint = ScopeMarker.Fingerprint(new Dictionary<string, string> { ["name"] = "MFA" });

        ScopeMarker.Read(ScopeMarker.Mark("Every user signs in with MFA.", fingerprint)).ShouldBe(("Every user signs in with MFA.", fingerprint));
        ScopeMarker.Read(ScopeMarker.Mark(null, fingerprint)).ShouldBe(((string?)null, fingerprint));
        ScopeMarker.Read("Written by hand.").ShouldBe(("Written by hand.", (string?)null));
        ScopeMarker.Read("").ShouldBe(((string?)null, (string?)null));
        fingerprint.ShouldMatch("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Fingerprint_ignores_field_order()
    {
        var one = new Dictionary<string, string> { ["name"] = "MFA", ["priority"] = "1" };
        var other = new Dictionary<string, string> { ["priority"] = "1", ["name"] = "MFA" };

        ScopeMarker.Fingerprint(one).ShouldBe(ScopeMarker.Fingerprint(other));
        ScopeMarker.Fingerprint(one).ShouldNotBe(ScopeMarker.Fingerprint(new Dictionary<string, string> { ["name"] = "MFA", ["priority"] = "2" }));
    }

    /// <summary>
    /// Whatever CISO Assistant holds, applying the plan and planning again leaves nothing to write, and the only drift left is objects
    /// <c>hound</c> does not manage and must not delete.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Planning_against_the_result_of_applying_its_own_plan_is_empty((byte Kind, byte Key, byte Value, byte State)[] picks)
    {
        var declared = (picks ?? []).Take(8)
            .Select(p => Declared((ScopeKind)(p.Kind % 8), $"key-{p.Key % 4}", ("name", $"value-{p.Value % 3}")))
            .DistinctBy(o => (o.Kind, o.Key))
            .ToArray();
        ActualObject[] actual = [.. (picks ?? []).Take(8).Select(p => (p.State % 4) switch
        {
            0 => new ActualObject(Declared((ScopeKind)(p.Kind % 8), $"key-{p.Key % 4}", ("name", $"held-{p.Value % 2}")), AppliedState: null),
            1 => Applied(Declared((ScopeKind)(p.Kind % 8), $"key-{p.Key % 4}", ("name", $"value-{p.Value % 3}"))),
            2 => new ActualObject(Declared((ScopeKind)(p.Kind % 8), $"key-{p.Key % 4}", ("name", "edited")), "0000000000000000"),
            _ => Applied(Declared((ScopeKind)(p.Kind % 8), $"other-{p.Key % 2}", ("name", "gone"))),
        }).DistinctBy(a => (a.Held.Kind, a.Held.Key))];

        var plan = ScopePlanner.Plan(declared, actual, prune: true);
        var replan = ScopePlanner.Plan(declared, InMemoryApply(actual, declared, plan), prune: true);

        replan.Changes.ShouldBeEmpty();
        replan.Drift.ShouldAllBe(d => d.Reason == DriftReason.NotInScope && !d.Managed);
    }

    /// <summary>What CISO Assistant holds once the plan is applied: created and updated objects carry their declared fields and the plan's marker.</summary>
    private static ActualObject[] InMemoryApply(ActualObject[] actual, ScopeObject[] declared, ScopePlan plan)
    {
        var held = actual.ToDictionary(a => (a.Held.Kind, a.Held.Key));
        foreach (var change in plan.Changes)
        {
            if (change.Action == ScopeAction.Delete)
            {
                held.Remove((change.Kind, change.Key));
                continue;
            }

            var fields = held.TryGetValue((change.Kind, change.Key), out var before)
                ? before.Held.Fields.ToDictionary(StringComparer.Ordinal)
                : [];
            var initial = declared.Single(d => (d.Kind, d.Key) == (change.Kind, change.Key)).Initial;
            foreach (var field in change.Fields)
            {
                if (field.New is null)
                {
                    fields.Remove(field.Field);
                }
                else if (!initial.ContainsKey(field.Field))
                {
                    fields[field.Field] = field.New;
                }
            }

            held[(change.Kind, change.Key)] = new ActualObject(new ScopeObject(change.Kind, change.Key, fields), change.State ?? before?.AppliedState);
        }

        return [.. held.Values];
    }

    private static ScopeObject Declared(ScopeKind kind, string key, params (string Field, string Value)[] fields) =>
        new(kind, key, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static ActualObject Applied(ScopeObject held) => new(held, ScopeMarker.Fingerprint(held.Fields));

    private sealed class ChangeComparer : IEqualityComparer<ScopeChange>
    {
        public bool Equals(ScopeChange? x, ScopeChange? y) =>
            x is not null && y is not null && (x.Kind, x.Key, x.Action, x.State) == (y.Kind, y.Key, y.Action, y.State) && x.Fields.SequenceEqual(y.Fields);

        public int GetHashCode(ScopeChange obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }

    private sealed class DriftComparer : IEqualityComparer<ScopeDrift>
    {
        public bool Equals(ScopeDrift? x, ScopeDrift? y) =>
            x is not null && y is not null && (x.Kind, x.Key, x.Reason, x.Managed) == (y.Kind, y.Key, y.Reason, y.Managed) && x.Fields.SequenceEqual(y.Fields);

        public int GetHashCode(ScopeDrift obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }
}
