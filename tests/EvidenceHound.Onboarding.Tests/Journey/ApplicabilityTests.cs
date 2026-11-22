using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>One proposal per in-scope requirement; the person decides each, and not applicable needs a justification and an approver.</summary>
public sealed class ApplicabilityTests
{
    internal static readonly FrameworkSnapshot Framework = new(
        "urn:example:library",
        "urn:example:framework",
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two")],
        [
            new SnapshotNode("urn:example:req:a", "EX-1", null, ["1", "2"], "Patch internet-facing services within two weeks."),
            new SnapshotNode("urn:example:req:b", "EX-2", null, ["2"], "Multi-factor authentication for privileged users."),
            new SnapshotNode("urn:example:req:c", "EX-3", null, ["3"], "Level three only."),
        ]);

    [Fact]
    public void Every_in_scope_requirement_gets_one_decision()
    {
        var requirements = Applicability.InScope(Framework, ["2"]);
        requirements.Select(r => r.Key).ShouldBe(["EX-1", "EX-2"]);

        var reply = JsonNode.Parse("""{"decisions":[{"ref":"EX-2","kind":"api_check","reasoning":"GET /v1/users shows MFA.","other_system":null}]}""")!;
        var proposals = Applicability.Proposals(requirements, reply).Value();

        proposals.Select(p => (p.Key, p.Kind)).ShouldBe([("EX-1", (DecisionKind?)null), ("EX-2", DecisionKind.ApiCheck)]);
        proposals[1].Reasoning.ShouldBe("GET /v1/users shows MFA.");
        Applicability.Empty(requirements).Select(p => p.Kind).ShouldBe([null, null]);
    }

    [Fact]
    public void A_reply_naming_an_unknown_or_repeated_requirement_is_invalid()
    {
        var requirements = Applicability.InScope(Framework, ["2"]);

        var unknown = JsonNode.Parse("""{"decisions":[{"ref":"EX-9","kind":"manual","reasoning":"x","other_system":null}]}""")!;
        Applicability.Proposals(requirements, unknown).Problems().ShouldHaveSingleItem().Code.ShouldBe("llm.invalid_reply");

        var repeated = JsonNode.Parse("""{"decisions":[{"ref":"EX-1","kind":"manual","reasoning":"x","other_system":null},{"ref":"EX-1","kind":"api_check","reasoning":"y","other_system":null}]}""")!;
        Applicability.Proposals(requirements, repeated).Problems().ShouldHaveSingleItem().Message.ShouldContain("EX-1");
    }

    [Fact]
    public void No_groups_means_every_requirement()
    {
        Applicability.InScope(Framework, []).Count.ShouldBe(3);
    }

    [Fact]
    public void Not_applicable_without_justification_or_approver_is_refused()
    {
        var proposal = Applicability.Empty(Applicability.InScope(Framework, ["2"]))[0];

        Applicability.Decide(proposal, DecisionKind.NotApplicable, "No internet-facing services.", "Alex Example", null, "Sam Example", null)
            .Problems().ShouldHaveSingleItem().Code.ShouldBe("onboard.justification_missing");
        Applicability.Decide(proposal, DecisionKind.NotApplicable, "No internet-facing services.", "Alex Example", "The system has no public endpoint.", " ", null)
            .Problems().ShouldHaveSingleItem().Code.ShouldBe("onboard.approver_missing");

        var decision = Applicability.Decide(proposal, DecisionKind.NotApplicable, "No internet-facing services.", "Alex Example", "The system has no public endpoint.", "Sam Example", null).Value();
        decision.Kind.ShouldBe(DecisionKind.NotApplicable);
        decision.Justification.ShouldBe("The system has no public endpoint.");
        decision.Approver.ShouldBe("Sam Example");
        decision.Proposed.ShouldBeNull();
    }

    [Fact]
    public void Another_system_must_be_named()
    {
        var proposal = Applicability.Empty(Applicability.InScope(Framework, ["2"]))[1];

        Applicability.Decide(proposal, DecisionKind.OtherSystem, "MFA is enforced by the identity provider.", "Alex Example", null, null, null)
            .Problems().ShouldHaveSingleItem().Code.ShouldBe("onboard.other_system_missing");
        Applicability.Decide(proposal, DecisionKind.OtherSystem, "MFA is enforced by the identity provider.", "Alex Example", null, null, "Example identity provider")
            .Value().OtherSystem.ShouldBe("Example identity provider");
    }

    [Fact]
    public async Task Proposals_are_asked_in_batches_with_the_applicability_skills()
    {
        var model = new ScriptedModel(request =>
        {
            var refs = request.Inputs.Single(i => i.Name == "requirements").Content.Split('\n').Select(line => line.Split(':')[0]).ToArray();
            return new JsonObject { ["decisions"] = new JsonArray([.. refs.Select(r => new JsonObject { ["ref"] = r, ["kind"] = "manual", ["reasoning"] = "Ask the owner.", ["other_system"] = null })]) };
        });
        var skills = SkillSelection.Select(SkillCatalogue.Defaults(), SkillStep.Applicability, "essential-eight-2023", SkillSelection.DefaultBudgetCharacters).Value();

        var proposals = await Applicability.ProposeAsync(model, skills, Applicability.InScope(Framework, []), new SystemFacts("Example asset system", "Example Asset API", "Alex Example"), "GET /v1/devices", batch: 2, TestContext.Current.CancellationToken);

        proposals.Value().Count.ShouldBe(3);
        model.Sent.Count.ShouldBe(2);
        model.Sent.ShouldAllBe(r => r.Step == "applicability");
        model.Sent[0].Instructions.ShouldContain("<skill name=\"applicability\"");
        model.Sent[0].Inputs.Select(i => i.Name).ShouldBe(["system", "api", "requirements"]);
        model.Sent[0].Inputs[2].Content.ShouldBe("EX-1: Patch internet-facing services within two weeks.\nEX-2: Multi-factor authentication for privileged users.");
    }

    /// <summary>A model that answers each request with what the script returns, and keeps every request.</summary>
    internal sealed class ScriptedModel(Func<DraftRequest, JsonNode> reply) : IDraftingModel
    {
        public List<DraftRequest> Sent { get; } = [];

        public Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return Task.FromResult(Result.Ok(new DraftReply(reply(request), 10, 10)));
        }
    }
}
