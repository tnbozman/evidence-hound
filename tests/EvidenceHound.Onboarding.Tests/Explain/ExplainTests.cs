using System.Text.Json.Nodes;
using EvidenceHound.Onboarding.Explain;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Tests.Journey;

namespace EvidenceHound.Onboarding.Tests.Explain;

/// <summary>
/// <c>hound explain</c>: the plan or diff is read in code first (counts, objects, what needs a careful look); the model only adds a review
/// summary, checked against what the plan contains.
/// </summary>
public sealed class ExplainTests
{
    internal const string Plan = """
        ## Scope plan

        2 to create, 1 to update, 1 to delete. No drift.

        | Change | Kind | Object | Fields |
        | --- | --- | --- | --- |
        | Create | audit | EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023 | name: EXAMPLE-ASSETS essential-eight-2023 |
        | Create | assessment | EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2 | result: not_applicable |
        | Update | contract | CTR-EXAMPLE | status: draft → active |
        | Delete | control | CTL-OLD |  |

        ### Access review

        | User | Note |
        | --- | --- |
        | alex@corp.example.org | MFA is off |
        """;

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Summary_counts_match_the_plan()
    {
        var facts = ChangeReader.Read(Plan, "plan.md").Value();

        facts.CountsText.ShouldBe("2 to create, 1 to update, 1 to delete, 0 drifted.");
        facts.Objects.ShouldBe(["EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023", "EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2", "CTR-EXAMPLE", "CTL-OLD"]);
        facts.Topics.ShouldBe([ChangeTopic.Audit, ChangeTopic.NotApplicable, ChangeTopic.ThirdParty], ignoreOrder: true);
        facts.Flags.ShouldContain("Deletes control CTL-OLD.");
        facts.Flags.ShouldContain("Marks EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2 not applicable.");

        var reply = Explanation.Read(facts, Reply("Adds the asset system's audit.", ("CTL-OLD", "Removed; check nothing still links to it."))).Value();
        var text = Explanation.Render(facts, reply, []);

        text.ShouldContain("The plan is the source of truth: 2 to create, 1 to update, 1 to delete, 0 drifted.");
        text.ShouldContain("- Deletes control CTL-OLD.");
        text.ShouldContain("Adds the asset system's audit.");
        text.ShouldContain("- CTL-OLD: Removed; check nothing still links to it.");

        // A JSON scope plan and a diff give their own counts.
        const string json = """{"changes":[{"kind":"contract","key":"CTR-EXAMPLE","action":"delete","fields":[],"state":"a1b2"}],"drift":[]}""";
        ChangeReader.Read(json, "plan.json").Value().CountsText.ShouldBe("0 to create, 0 to update, 1 to delete, 0 drifted.");
        var diff = ChangeReader.Diff("result: not_assessed\n", "result: not_applicable\n", "scope/audits/example.yaml");
        var diffFacts = ChangeReader.Read(diff, "diff").Value();
        diffFacts.CountsText.ShouldBe("1 file changed, 1 line added, 1 line removed.");
        diffFacts.Flags.ShouldContain("Marks 1 requirement(s) not applicable in scope/audits/example.yaml.");
        diffFacts.Topics.ShouldBe([ChangeTopic.Audit, ChangeTopic.NotApplicable], ignoreOrder: true);

        // Without a model the deterministic sections stand alone.
        Explanation.Render(facts, null, []).ShouldContain("No model summary (--no-model).");
    }

    [Fact]
    public void Object_not_in_the_plan_is_an_invalid_reply()
    {
        var facts = ChangeReader.Read(Plan, "plan.md").Value();

        var problem = Explanation.Read(facts, Reply("Removes a control.", ("CTL-MADE-UP", "Gone."))).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("llm.invalid_reply");
        problem.Message.ShouldContain("CTL-MADE-UP");
        Explanation.Read(facts, new JsonObject { ["summary"] = 3 }).Problems().ShouldHaveSingleItem().Code.ShouldBe("llm.invalid_reply");
    }

    [Fact]
    public async Task Secrets_and_identifiers_are_never_sent()
    {
        const string secret = "sk-test-not-a-real-key";
        var plan = Plan + $"\n| Create | setting | {secret} on 10.1.2.3 | id: 3f2b8c1e-9a7d-4e21-8b55-0c6f1d2a9e44 |\n";
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply("Fine."));
        var terms = new GuardTerms(new Dictionary<string, string> { ["llm_api_key"] = secret }, []);

        (await Explanation.DraftAsync(model, [], ChangeReader.Read(plan, "plan.md").Value(), terms, _ct)).Value();

        var sent = string.Join('\n', model.Sent.SelectMany(r => r.Inputs.Select(i => i.Content)).Append(model.Sent[0].Instructions));
        sent.ShouldNotContain(secret);
        sent.ShouldNotContain("alex@corp.example.org");
        sent.ShouldNotContain("10.1.2.3");
        sent.ShouldNotContain("3f2b8c1e-9a7d-4e21-8b55-0c6f1d2a9e44");
        OutboundGuard.Check(model.Sent[0], terms).ShouldBeEmpty();
        model.Sent[0].Step.ShouldBe("explain");
        sent.ShouldContain("2 to create");
    }

    internal static JsonObject Reply(string summary, params (string Object, string Note)[] notes) => new()
    {
        ["summary"] = summary,
        ["notes"] = new JsonArray([.. notes.Select(n => (JsonNode)new JsonObject { ["object"] = n.Object, ["note"] = n.Note })]),
    };
}
