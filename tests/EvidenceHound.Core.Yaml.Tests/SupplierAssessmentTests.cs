using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s09-20: a supplier's assessments, declared in its file, set up on a review cycle; answers and conclusions stay CISO Assistant's.</summary>
public class SupplierAssessmentTests
{
    internal const string Assessed = SupplierScopeTests.Supplier + """

        forced_tier:
          tier: critical
          reason: Hosts the customer portal.
        assessments:
          - ref_id: ENA-EXCLOUD
            name: Example Cloud review
            solutions: [SOL-EXCLOUD-HOST]
            perimeter: Example baseline
            due_date: "2026-03-31"
            criticality: 3
            questionnaire: urn:example:risk:framework:example-baseline
            groups: ["1"]
            review_every: 12 months
        """;

    private static readonly DateOnly Today = new(2026, 11, 22);

    [Fact]
    public void Due_review_opens_the_next_one()
    {
        var entity = Parse(Assessed).Scope.ThirdParties.ShouldHaveSingleItem();
        var assessment = entity.Assessments.ShouldHaveSingleItem();
        (assessment.RefId, assessment.ReviewMonths, assessment.DueDate).ShouldBe(("ENA-EXCLOUD", 12, "2026-03-31"));
        (entity.ForcedTier, entity.ForcedTierReason).ShouldBe(("critical", "Hosts the customer portal."));
        Problems(Assessed).ShouldBeEmpty();

        // Before the first review is due there is one assessment; once it falls due, the next is opened, a review cycle later.
        Reviews(new DateOnly(2026, 2, 1)).ShouldBe([("ENA-EXCLOUD-2026-03", "2026-03-31")]);
        Reviews(Today).ShouldBe([("ENA-EXCLOUD-2026-03", "2026-03-31"), ("ENA-EXCLOUD-2027-03", "2027-03-31")]);

        // Without a cycle, the assessment is declared once under its own ref_id.
        var once = Assessed.Replace("\n    review_every: 12 months", "", StringComparison.Ordinal);
        Desired(once, Today).Single(o => o.Kind == ScopeKind.EntityAssessment).Key.ShouldBe("ENA-EXCLOUD");
    }

    [Fact]
    public void Answers_are_never_in_the_plan()
    {
        var review = Desired(Assessed, Today).First(o => o.Kind == ScopeKind.EntityAssessment);

        review.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Cloud review 2026-03",
            ["entity"] = "SUP-EXCLOUD",
            ["solutions"] = "SOL-EXCLOUD-HOST",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Example baseline",
            ["due_date"] = "2026-03-31",
            ["criticality"] = "3",
        });

        // The questionnaire is set up once, when the assessment is created; its answers, the conclusion and the tier are CISO Assistant's.
        review.Initial.ShouldBe(new Dictionary<string, string> { ["questionnaire"] = "urn:example:risk:framework:example-baseline", ["groups"] = "1" });
        string[] owned = ["conclusion", "expiry_date", "answers", "tier", "compliance_assessment"];
        Desired(Assessed, Today).SelectMany(o => o.Fields.Keys.Concat(o.Initial.Keys)).ShouldNotContain(k => owned.Contains(k));
    }

    [Fact]
    public void Conclusion_in_the_file_is_declared_on_its_review_with_who_and_when()
    {
        const string conclusions = "\n    conclusions:\n      - review: ENA-EXCLOUD-2026-03\n        result: ok\n        by: Sam Example\n        on: \"2026-04-02\"\n";
        var concluded = Assessed + conclusions;

        Problems(concluded).ShouldBeEmpty();
        var conclusion = Parse(concluded).Scope.ThirdParties.Single().Assessments.Single().Conclusions.ShouldHaveSingleItem();
        (conclusion.Review, conclusion.Result, conclusion.By, conclusion.On).ShouldBe(("ENA-EXCLOUD-2026-03", "ok", "Sam Example", "2026-04-02"));

        // The review it names carries the conclusion to CISO Assistant; the next review stays open.
        var reviews = Desired(concluded, Today).Where(o => o.Kind == ScopeKind.EntityAssessment).ToArray();
        reviews.Select(r => (r.Key, r.Fields.GetValueOrDefault("conclusion"))).ShouldBe([("ENA-EXCLOUD-2026-03", "ok"), ("ENA-EXCLOUD-2027-03", null)]);

        // Who and when are required, the result is ok, warning or blocker, and the review is one of this assessment's.
        var broken = concluded
            .Replace("result: ok", "result: fine", StringComparison.Ordinal)
            .Replace("        by: Sam Example\n", "", StringComparison.Ordinal)
            .Replace("on: \"2026-04-02\"", "on: last week", StringComparison.Ordinal);
        Problems(broken).Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["scope.key_missing", "scope.value_invalid", "scope.value_invalid"]);
        var other = Problems(concluded.Replace("review: ENA-EXCLOUD-2026-03", "review: ENA-OTHER-2026-03", StringComparison.Ordinal)).ShouldHaveSingleItem();
        other.Code.ShouldBe("supplier.conclusion_review_unknown");
        other.Message.ShouldContain("ENA-OTHER-2026-03");

        // Without a cycle the one review is the assessment itself.
        var once = concluded.Replace("\n    review_every: 12 months", "", StringComparison.Ordinal).Replace("review: ENA-EXCLOUD-2026-03", "review: ENA-EXCLOUD", StringComparison.Ordinal);
        Problems(once).ShouldBeEmpty();
        Desired(once, Today).Single(o => o.Kind == ScopeKind.EntityAssessment).Fields["conclusion"].ShouldBe("ok");
    }

    [Fact]
    public void A_contract_records_who_approved_the_supplier_and_when()
    {
        var approved = Assessed.Replace("    status: active\n", "    status: active\n    approved_by: Sam Example\n    approved_on: \"2026-01-05\"\n", StringComparison.Ordinal);
        approved.ShouldNotBe(Assessed);

        Problems(approved).ShouldBeEmpty();
        var contract = Parse(approved).Scope.ThirdParties.Single().Contracts[0];
        (contract.ApprovedBy, contract.ApprovedOn).ShouldBe(("Sam Example", "2026-01-05"));

        // Who approved is hound's record, not a field CISO Assistant holds.
        Desired(approved, Today).Where(o => o.Kind == ScopeKind.Contract).SelectMany(o => o.Fields.Keys).ShouldNotContain(k => k.StartsWith("approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Assessment_references_are_checked()
    {
        var broken = Assessed
            .Replace("solutions: [SOL-EXCLOUD-HOST]\n    perimeter", "solutions: [SOL-OTHER]\n    perimeter", StringComparison.Ordinal)
            .Replace("perimeter: Example baseline", "perimeter: Nowhere", StringComparison.Ordinal)
            .Replace("questionnaire: urn:example:risk:framework:example-baseline", "questionnaire: urn:example:unknown", StringComparison.Ordinal)
            .Replace("review_every: 12 months", "review_every: fortnightly", StringComparison.Ordinal);

        Problems(broken).Select(p => p.Code).Order(StringComparer.Ordinal)
            .ShouldBe(["scope.reference_unknown", "scope.reference_unknown", "scope.value_invalid", "supplier.solution_unknown"]);
    }

    [Fact]
    public void A_system_names_its_solution_and_who_supplies_it()
    {
        var setup = ScopeFixture.Setup.Replace(
            "        owner: it@example.com\n        tags:",
            "        owner: it@example.com\n        solution: SOL-EXCLOUD-HOST\n        supplied_through: SUP-EXCLOUD\n        tags:",
            StringComparison.Ordinal);
        var parsed = ScopeLoader.Parse(new ScopeSource("scope/setup.yaml", setup), null, [], thirdParties: [new ScopeSource(SupplierScopeTests.SupplierPath, Assessed)]);

        var system = parsed.Scope.Setup.Domains.Single().Systems[0];
        (system.Solution?.Value, system.SuppliedThrough?.Value).ShouldBe(("SOL-EXCLOUD-HOST", "SUP-EXCLOUD"));
        ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []).ShouldBeEmpty();

        var unknown = ScopeLoader.Parse(new ScopeSource("scope/setup.yaml", setup.Replace("solution: SOL-EXCLOUD-HOST", "solution: SOL-NOWHERE", StringComparison.Ordinal)), null, []);
        ScopeValidator.Validate(unknown.Scope, ScopeFixture.Snapshots, []).Select(p => p.Code).ShouldBe(["scope.reference_unknown", "scope.reference_unknown"]);
    }

    private static List<(string Key, string Due)> Reviews(DateOnly today) =>
        [.. Desired(Assessed, today).Where(o => o.Kind == ScopeKind.EntityAssessment).Select(o => (o.Key, o.Fields["due_date"]))];

    private static IReadOnlyList<ScopeObject> Desired(string supplier, DateOnly today) => ScopeDesired.From(Parse(supplier).Scope, ScopeInputs.None with { Today = today });

    private static ScopeParse Parse(string supplier) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            thirdParties: [new ScopeSource(SupplierScopeTests.SupplierPath, supplier)]);

    private static IReadOnlyList<ScopeProblem> Problems(string supplier)
    {
        var parsed = Parse(supplier);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, []), .. SupplierValidator.Validate(parsed.Scope, Today)];
    }
}
