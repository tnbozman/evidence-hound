using EvidenceHound.Core.Review;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Review;

/// <summary>s18-01: what needs doing now, most urgent first, each with the command or skill that handles it.</summary>
public class NextWorkTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static ReviewItem Item(string key, int minutes, DateOnly? deadline = null, bool recommended = false) =>
        new(ReviewKind.Supplier, key, $"Title of {key}", "Why.", "Effect.", minutes)
        {
            Deadline = deadline,
            Recommendation = recommended ? new ReviewRecommendation("accept", "Because.", ["scope/x.yaml"]) : null,
        };

    [Fact]
    public void Overdue_and_broken_come_before_what_is_merely_waiting()
    {
        NextItem[] items =
        [
            NextWork.Scope(changes: 3, drift: 0)!,
            NextWork.Collector("example-m365", lastRun: null, failed: false, problems: [], dueAfterLast: null, Now)!,
            NextWork.Manual("example-incident-register", "it@example.com", dueDays: 30, lastEvidence: Now.AddDays(-40), Today)!,
            NextWork.Exception(new ExceptionNote("exception.expiring", "security exception", "EXC-01", "Legacy app", "approved", "2026-12-20", "Renew or close it."), Today),
            .. NextWork.Review([Item("supplier:A", 10, recommended: true)], Today),
        ];

        var ordered = NextWork.Order(items);

        ordered.Select(i => i.Kind).ShouldBe(["manual_source", "collector", "exception", "review", "scope"]);
        ordered[0].Urgency.ShouldBe(NextUrgency.Overdue);
        ordered[0].Due.ShouldBe(Today.AddDays(-10));
        ordered.ShouldAllBe(i => i.Command.StartsWith("hound ", StringComparison.Ordinal) && i.Minutes > 0);
    }

    [Fact]
    public void The_review_queue_is_one_entry_with_its_count_and_minutes_and_one_for_what_lacks_a_recommendation()
    {
        var entries = NextWork.Review([Item("supplier:A", 10, recommended: true), Item("supplier:B", 5), Item("supplier:C", 5, Today.AddDays(-1))], Today);

        entries.Count.ShouldBe(2);
        var review = entries.Single(e => e.Kind == "review");
        review.Urgency.ShouldBe(NextUrgency.Overdue, "one item is past its deadline");
        review.Subject.ShouldBe("3 item(s) wait on a person, about 20 minutes");
        review.Command.ShouldBe("hound review --for 30m");
        review.Skill.ShouldBe("work-through-pending-decisions");
        var recommend = entries.Single(e => e.Kind == "recommendation");
        recommend.Subject.ShouldBe("2 item(s) have no recommendation yet");
        recommend.Command.ShouldBe("hound review pending --json");
        recommend.Skill.ShouldBe("weekly-review");

        NextWork.Review([], Today).ShouldBeEmpty();
    }

    [Fact]
    public void A_collector_that_ran_on_time_and_passed_needs_nothing()
    {
        NextWork.Collector("example-m365", Now.AddHours(-9), failed: false, problems: [], dueAfterLast: Now.AddHours(15), Now).ShouldBeNull();

        var failed = NextWork.Collector("example-m365", Now.AddHours(-9), failed: true, problems: ["All 2 checks errored."], dueAfterLast: Now.AddHours(15), Now)!;
        failed.Urgency.ShouldBe(NextUrgency.Broken);
        failed.Detail.ShouldContain("All 2 checks errored.");
        failed.Command.ShouldBe("hound run --config hound.yaml --only example-m365");
        failed.Skill.ShouldBe("operate-collectors");

        var late = NextWork.Collector("example-m365", Now.AddDays(-3), failed: false, problems: [], dueAfterLast: Now.AddDays(-2), Now)!;
        late.Urgency.ShouldBe(NextUrgency.Overdue);
        late.Detail.ShouldContain("2026-11-23");
    }

    [Fact]
    public void A_manual_source_is_listed_from_two_weeks_before_it_falls_due()
    {
        NextWork.Manual("src", "it@example.com", 90, Now.AddDays(-60), Today).ShouldBeNull();
        NextWork.Manual("src", "it@example.com", 90, Now.AddDays(-80), Today)!.Urgency.ShouldBe(NextUrgency.DueSoon);
        var never = NextWork.Manual("src", "it@example.com", 90, null, Today)!;
        never.Urgency.ShouldBe(NextUrgency.Overdue);
        never.Detail.ShouldContain("No evidence uploaded yet");
        never.Command.ShouldBe("hound manual upload <file> --config hound.yaml --source src");
    }

    [Fact]
    public void An_expired_exception_is_overdue_and_one_expiring_is_due_soon()
    {
        NextWork.Exception(new ExceptionNote("exception.expired", "security exception", "EXC-01", "Legacy app", "approved", "2026-11-01", "Renew or close it."), Today)
            .Urgency.ShouldBe(NextUrgency.Overdue);
        NextWork.Exception(new ExceptionNote("exception.expiring", "risk acceptance", "RA-01", "Old server", "approved", "2026-12-20", "Renew or close it."), Today)
            .Urgency.ShouldBe(NextUrgency.DueSoon);
    }

    [Fact]
    public void Nothing_to_apply_is_not_listed()
    {
        NextWork.Scope(0, 0).ShouldBeNull();
        NextWork.Scope(0, 2)!.Subject.ShouldBe("0 change(s) to apply and 2 drifted value(s)");
    }
}
