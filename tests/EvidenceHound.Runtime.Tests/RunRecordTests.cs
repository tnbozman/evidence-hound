using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Runtime.Tests;

public class RunRecordTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddSeconds(42);

    private static CheckResult Check(string id, CheckOutcome outcome) =>
        CheckResult.Create(CheckId.Create(id).Value(), outcome, "x", [], Start, "idp").Value();

    [Fact]
    public void Summarises_outcomes()
    {
        CheckResult[] checks = [Check("a.one", CheckOutcome.Pass), Check("a.two", CheckOutcome.Fail), Check("a.three", CheckOutcome.Partial), Check("a.four", CheckOutcome.Pass)];
        var update = new AuditUpdate.UpdateAppliedControl("AC-MFA", ControlState.Active);
        var report = new PublishReport([
            PublishItem.For(update, Result.Ok(PublishOutcome.Sent)),
            PublishItem.For(update, Result.Ok(PublishOutcome.Skipped)),
            PublishItem.For(update, Result.Fail<PublishOutcome>(new Problem("ciso_assistant.rejected", "no"))),
        ]);
        Problem[] problems = [new("ciso_assistant.rejected", "no")];

        var record = RunRecords.Summarise("idp", Start, End, CollectorOutcome.Failed, checks, problems, report);

        record.ShouldBe(new RunRecord("idp", Start, End, CollectorOutcome.Failed, 2, 1, 1, problems, new PublishCounts(0, 1, 1, 1)));
        RunRecords.Summarise("idp", Start, End, CollectorOutcome.Succeeded, [], [], null).Publish.ShouldBeNull();
    }

    [Fact]
    public void Json_round_trips()
    {
        var record = new RunRecord("idp", Start, End, CollectorOutcome.Failed, 2, 1, 1, [new("collector.unavailable", "down")], new PublishCounts(8, 6, 2, 0));

        RunRecords.Parse(RunRecords.ToJson(record), "idp.json").Value().ShouldBe(record);
        RunRecords.Parse(RunRecords.ToJson(record with { Publish = null }), "idp.json").Value().Publish.ShouldBeNull();
    }

    [Fact]
    public void Json_uses_snake_case_and_lowercase_outcomes()
    {
        var json = RunRecords.ToJson(new RunRecord("idp", Start, End, CollectorOutcome.Succeeded, 1, 0, 0, [], null));

        json.ShouldContain("\"collector\": \"idp\"");
        json.ShouldContain("\"outcome\": \"succeeded\"");
        json.ShouldContain("\"checks_passed\": 1");
        json.ShouldContain("\"started_at\": \"2026-06-30T02:00:00+00:00\"");
    }

    [Fact]
    public void Unreadable_json_is_a_problem_naming_the_source()
    {
        var problem = RunRecords.Parse("{", "idp.json").Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("status.record_invalid");
        problem.Message.ShouldContain("idp.json");
    }
}
