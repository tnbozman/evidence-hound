using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>
/// s09-20: a system is registered only once every supplier it names is approved in CISO Assistant: its latest assessment concluded ok and in
/// date, and an active contract. Approval is read, never written; a lapsed approval is reported, not undone.
/// </summary>
public class SupplierGateTests
{
    internal static readonly DateOnly Today = new(2026, 11, 22);
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    [Fact]
    public void System_with_unapproved_supplier_is_not_registered()
    {
        var scope = Scope(System("Example portal", "SOL-HOST", null));

        // Nothing concluded yet.
        var problem = SupplierReview.Check(scope, [Contract("CTR-HOST", "SUP-HOST", "active")], Today).Refused.ShouldHaveSingleItem();
        problem.Code.ShouldBe("system.supplier_not_approved");
        problem.Message.ShouldContain("Example portal");
        problem.Message.ShouldContain("SUP-HOST");
        problem.Message.ShouldContain("no concluded assessment");

        // Concluded with a blocker, or ok but without an active contract: still refused, saying what is missing.
        SupplierReview.Check(scope, [Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST", "SUP-HOST", "2026-06-30", "blocker", null)], Today)
            .Refused.ShouldHaveSingleItem().Message.ShouldContain("concluded blocker");
        SupplierReview.Check(scope, [Contract("CTR-HOST", "SUP-HOST", "draft"), Assessment("ENA-HOST", "SUP-HOST", "2026-06-30", "ok", null)], Today)
            .Refused.ShouldHaveSingleItem().Message.ShouldContain("no active contract");

        // Concluded ok, in date, with an active contract: approved.
        var approved = SupplierReview.Check(scope, [Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST", "SUP-HOST", "2026-06-30", "ok", "2027-06-30")], Today);
        approved.Refused.ShouldBeEmpty();
        approved.Notes.ShouldBeEmpty();
    }

    [Fact]
    public void Reseller_named_by_supplied_through_must_also_be_approved()
    {
        var scope = Scope(System("Example portal", "SOL-HOST", "SUP-RESELLER"));
        ActualObject[] supplierOnly = [Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST", "SUP-HOST", "2026-06-30", "ok", null)];

        var problem = SupplierReview.Check(scope, supplierOnly, Today).Refused.ShouldHaveSingleItem();
        problem.Message.ShouldContain("SUP-RESELLER");
        problem.Message.ShouldNotContain("SUP-HOST is");

        SupplierReview.Check(scope, [.. supplierOnly, Contract("CTR-RESELLER", "SUP-RESELLER", "active"), Assessment("ENA-RESELLER", "SUP-RESELLER", "2026-06-30", "ok", null)], Today)
            .Refused.ShouldBeEmpty();
    }

    [Fact]
    public void Lapsed_approval_is_reported_not_unregistered()
    {
        var scope = Scope(System("Example portal", "SOL-HOST", null));
        var registered = new ActualObject(new ScopeObject(ScopeKind.System, "Example portal", new Dictionary<string, string>()), "abc");

        // The conclusion expired: the registered system stays, and is listed for the committee.
        var expired = SupplierReview.Check(scope, [registered, Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST", "SUP-HOST", "2025-06-30", "ok", "2026-06-30")], Today);
        expired.Refused.ShouldBeEmpty();
        var note = expired.Notes.ShouldHaveSingleItem();
        (note.Code, note.Supplier, note.System).ShouldBe(("supplier.approval_lapsed", "SUP-HOST", "Example portal"));
        note.Detail.ShouldContain("expired on 2026-06-30");

        // The next review fell due and is not concluded.
        var overdue = SupplierReview.Check(
            scope,
            [registered, Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST-2025-10", "SUP-HOST", "2025-10-31", "ok", null), Assessment("ENA-HOST-2026-10", "SUP-HOST", "2026-10-31", null, null)],
            Today);
        overdue.Notes.ShouldHaveSingleItem().Detail.ShouldContain("ENA-HOST-2026-10 was due on 2026-10-31");

        // An open review that is not yet due keeps the previous approval.
        SupplierReview.Check(
            scope,
            [registered, Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST-2025-12", "SUP-HOST", "2025-12-31", "ok", null), Assessment("ENA-HOST-2026-12", "SUP-HOST", "2026-12-31", null, null)],
            Today).Notes.ShouldBeEmpty();
    }

    [Fact]
    public void Approval_recorded_in_config_registers_the_system_with_its_supplier_in_one_apply()
    {
        // s09-37: the person approved the supplier with hound approve, so the active contract and the review's conclusion are in the scope,
        // and CISO Assistant holds neither yet.
        ScopeEntity Host(string contract, string? result) => new("SUP-HOST", "Example Hosting", At)
        {
            Solutions = [new ScopeSolution("SOL-HOST", "Managed hosting", null, 3, At)],
            Contracts = [new ScopeContract("CTR-HOST", "Hosting agreement", contract, At)],
            Assessments =
            [
                new ScopeEntityAssessment("ENA-HOST", "Example Hosting review", new ScopeRef("Suppliers", At), At)
                {
                    DueDate = "2026-12-22",
                    ReviewMonths = 12,
                    Conclusions = result is null ? [] : [new ScopeConclusion("ENA-HOST-2026-12", result, "Sam Example", "2026-11-22", At)],
                },
            ],
        };
        ScopeDocument Declared(string contract, string? result)
        {
            var scope = Scope(System("Example portal", "SOL-HOST", null));
            return scope with { ThirdParties = [Host(contract, result), .. scope.ThirdParties.Where(e => e.RefId != "SUP-HOST")] };
        }

        SupplierReview.Check(Declared("active", "ok"), [], Today).Refused.ShouldBeEmpty();

        // A draft contract, no conclusion, or a blocker in config still refuses the system.
        SupplierReview.Check(Declared("draft", "ok"), [], Today).Refused.ShouldHaveSingleItem().Message.ShouldContain("no active contract");
        SupplierReview.Check(Declared("active", null), [], Today).Refused.ShouldHaveSingleItem().Message.ShouldContain("no concluded assessment");
        SupplierReview.Check(Declared("active", "blocker"), [], Today).Refused.ShouldHaveSingleItem().Message.ShouldContain("ENA-HOST-2026-12 concluded blocker");

        // The conclusion in config wins over what CISO Assistant holds for the same review.
        SupplierReview.Check(Declared("active", "blocker"), [Contract("CTR-HOST", "SUP-HOST", "active"), Assessment("ENA-HOST-2026-12", "SUP-HOST", "2026-12-22", "ok", null)], Today)
            .Refused.ShouldHaveSingleItem().Message.ShouldContain("concluded blocker");
        SupplierReview.Check(Declared("active", "blocker"), [], Today).Refused.Single().Hint.ShouldContain("hound approve");
    }

    internal static ScopeDocument Scope(ScopeSystem system, params ScopeEntity[] more) =>
        new(new ScopeSetup("Global", [], [new ScopeDomain("Example Pty Ltd", null, [], [system], At)]), [], [])
        {
            ThirdParties =
            [
                new ScopeEntity("SUP-HOST", "Example Hosting", At) { Solutions = [new ScopeSolution("SOL-HOST", "Managed hosting", null, 3, At)] },
                new ScopeEntity("SUP-RESELLER", "Example Reseller", At),
                .. more,
            ],
        };

    internal static ScopeSystem System(string name, string? solution, string? through) =>
        new(name, null, SystemType.Primary, null, "it@example.com", [], new Dictionary<string, string>(), At)
        {
            Solution = solution is null ? null : new ScopeRef(solution, At),
            SuppliedThrough = through is null ? null : new ScopeRef(through, At),
        };

    internal static ActualObject Contract(string refId, string entity, string status) =>
        new(new ScopeObject(ScopeKind.Contract, refId, new Dictionary<string, string> { ["entity"] = entity, ["status"] = status }), "abc");

    internal static ActualObject Assessment(string refId, string entity, string due, string? conclusion, string? expiry) =>
        new(new ScopeObject(ScopeKind.EntityAssessment, refId, new Dictionary<string, string> { ["entity"] = entity, ["due_date"] = due }), "abc")
        {
            Observed = new Dictionary<string, string?> { ["conclusion"] = conclusion, ["expiry_date"] = expiry }
                .Where(o => o.Value is not null).ToDictionary(o => o.Key, o => o.Value!, StringComparer.Ordinal),
        };
}
