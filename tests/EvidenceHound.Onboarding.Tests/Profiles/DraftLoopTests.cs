using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Profiles;
using EvidenceHound.Onboarding.Tests.Journey;

namespace EvidenceHound.Onboarding.Tests.Profiles;

/// <summary>s09-23: validation and test problems go back to the model until the draft is clean or the rounds are used up.</summary>
public sealed class DraftLoopTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Validation_problems_are_fed_back_until_clean_or_rounds_used()
    {
        var drafts = new Queue<string>(["round: 1\n", "round: 2\n", "round: 3\n"]);
        var model = new ApplicabilityTests.ScriptedModel(_ => new JsonObject { ["profile_yaml"] = drafts.Dequeue(), ["notes"] = "" });
        var checkedDrafts = new List<string>();

        var outcome = (await ProfileDraft.RunAsync(model, [], ProfileDraftTests.Context, (yaml, _) =>
        {
            checkedDrafts.Add(yaml);
            return Task.FromResult<IReadOnlyList<Problem>>(yaml == "round: 2\n" ? [] : [new Problem("profile.path_invalid", $"wrong path in {yaml.Trim()}")]);
        }, maxRounds: 3, Token)).Value();

        (outcome.Yaml, outcome.Rounds, outcome.Remaining.Count).ShouldBe(("round: 2\n", 2, 0));
        checkedDrafts.ShouldBe(["round: 1\n", "round: 2\n"]);
        model.Sent.Count.ShouldBe(2);
        model.Sent[1].Inputs.Single(i => i.Name == "problems").Content.ShouldBe("profile.path_invalid: wrong path in round: 1");
        model.Sent[1].Inputs.Single(i => i.Name == "previous draft").Content.ShouldBe("round: 1\n");
    }

    [Fact]
    public async Task Remaining_problems_are_kept_when_the_rounds_are_used_up()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => new JsonObject { ["profile_yaml"] = "still: wrong\n", ["notes"] = "" });

        var outcome = (await ProfileDraft.RunAsync(
            model, [], ProfileDraftTests.Context, (_, _) => Task.FromResult<IReadOnlyList<Problem>>([new Problem("profile.path_invalid", "wrong path")]), maxRounds: 2, Token)).Value();

        (outcome.Rounds, outcome.Remaining.Single().Code).ShouldBe((2, "profile.path_invalid"));
        model.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_invalid_reply_stops_the_loop()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => new JsonObject { ["notes"] = "" });

        var result = await ProfileDraft.RunAsync(model, [], ProfileDraftTests.Context, (_, _) => Task.FromResult<IReadOnlyList<Problem>>([]), maxRounds: 3, Token);

        result.ShouldBeOfType<Result<DraftOutcome>.Fail>().Errors.Single().Code.ShouldBe("llm.invalid_reply");
    }
}
