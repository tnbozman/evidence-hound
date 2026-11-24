using EvidenceHound.Core.Review;

namespace EvidenceHound.Core.Tests.Review;

/// <summary>s10-07: everything that waits on a person, in one queue, most urgent first and cut to the time the person has.</summary>
public class ReviewQueueTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    private static ReviewItem Item(ReviewKind kind, string key, int minutes, DateOnly? deadline = null) =>
        new(kind, key, $"Title of {key}", "Why it matters.", "What accepting does.", minutes) { Deadline = deadline };

    [Fact]
    public void A_supplier_review_waits_until_two_weeks_before_it_is_due()
    {
        // s10-05 rehearsal: four reviews due in a month took the top of a one-hour session, and none could be concluded yet.
        ReviewOrder.Ready(Item(ReviewKind.SupplierReview, "review:EA-NEXT-MONTH", 10, Today.AddDays(30)), Today).ShouldBeFalse();
        ReviewOrder.Ready(Item(ReviewKind.SupplierReview, "review:EA-SOON", 10, Today.AddDays(14)), Today).ShouldBeTrue();
        ReviewOrder.Ready(Item(ReviewKind.SupplierReview, "review:EA-LATE", 10, Today.AddDays(-3)), Today).ShouldBeTrue();
        ReviewOrder.Ready(Item(ReviewKind.ManualEvidence, "manual:admins/export.csv", 5, Today.AddDays(60)), Today).ShouldBeTrue();
        ReviewOrder.Ready(Item(ReviewKind.Supplier, "supplier:SUP-A/CTR-A", 10), Today).ShouldBeTrue();
    }

    [Fact]
    public void Most_urgent_first_and_stops_at_the_time_given()
    {
        ReviewItem[] items =
        [
            Item(ReviewKind.Audit, "audit:EX-1", 5),
            Item(ReviewKind.SupplierReview, "review:EA-LATE", 15, Today.AddDays(-2)),
            Item(ReviewKind.ManualEvidence, "manual:admins/export.csv", 5, Today.AddDays(10)),
            Item(ReviewKind.Supplier, "supplier:SUP-A/CTR-A", 10),
            Item(ReviewKind.NotApplicable, "not_applicable:LAPTOPS/ISM-1", 5),
        ];

        var ordered = ReviewOrder.Order(items);

        ordered.Select(i => i.Key).ShouldBe(
        [
            "review:EA-LATE",                 // past its deadline
            "manual:admins/export.csv",       // due in ten days
            "supplier:SUP-A/CTR-A",           // no deadline: suppliers first, as systems wait on them
            "not_applicable:LAPTOPS/ISM-1",
            "audit:EX-1",
        ]);
        ReviewOrder.Within(ordered, 30).Select(i => i.Key).ShouldBe(["review:EA-LATE", "manual:admins/export.csv", "supplier:SUP-A/CTR-A"]);
        ReviewOrder.Within(ordered, 1).Select(i => i.Key).ShouldBe(["review:EA-LATE"]);
        ReviewOrder.Within([], 60).ShouldBeEmpty();
    }

    [Fact]
    public void Each_kind_has_the_code_an_agent_reads()
    {
        Enum.GetValues<ReviewKind>().Select(ReviewOrder.Code).ShouldBe(
            ["capability", "supplier", "supplier_review", "not_applicable", "system", "audit", "imported_row", "manual_evidence", "finding"]);
    }
}
