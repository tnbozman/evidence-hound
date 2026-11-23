using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>A session is saved after every decision and resumes where it stopped, without asking the model again.</summary>
public sealed class OnboardSessionTests
{
    [Fact]
    public void Resume_keeps_accepted_decisions()
    {
        var requirements = Applicability.InScope(ApplicabilityTests.Framework, []);
        var proposals = Applicability.Empty(requirements);
        var first = Applicability.Decide(proposals[0], DecisionKind.Manual, "Ask the owner for the patch report.", "Alex Example", null, null, null).Value();
        var second = Applicability.Decide(proposals[1], DecisionKind.NotApplicable, "No privileged users in the product.", "Alex Example", "Only the supplier administers it.", "Sam Example", null).Value();
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Applicability)
        {
            Owner = "Alex Example",
            Product = "Example Asset API",
            Framework = "frameworks/essential-eight-2023.json",
            Groups = ["2"],
            Supplier = OnboardSupplierTests.Choice,
            Proposals = proposals,
        }.Decide(first).Decide(second);

        var resumed = OnboardSessionJson.Read(OnboardSessionJson.Write(session)).Value();

        resumed.Decisions.Select(d => (d.Key, d.Kind)).ShouldBe([("EX-1", DecisionKind.Manual), ("EX-2", DecisionKind.NotApplicable)]);
        resumed.Decisions[1].Approver.ShouldBe("Sam Example");
        resumed.Pending.Select(p => p.Key).ShouldBe(["EX-3"]);
        resumed.Groups.ShouldBe(["2"]);
        resumed.Supplier.ShouldBe(OnboardSupplierTests.Choice);
        resumed.Stage.ShouldBe(OnboardStage.Applicability);
    }

    [Fact]
    public void Deciding_again_replaces_the_earlier_decision_and_the_last_one_completes_the_session()
    {
        var proposals = Applicability.Empty(Applicability.InScope(ApplicabilityTests.Framework, ["2"]));
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Applicability) { Proposals = proposals };

        session = session.Decide(Applicability.Decide(proposals[0], DecisionKind.Manual, "First thought.", "Alex Example", null, null, null).Value());
        session = session.Decide(Applicability.Decide(proposals[0], DecisionKind.ApiCheck, "GET /v1/devices.", "Alex Example", null, null, null).Value());
        session.Decisions.ShouldHaveSingleItem().Kind.ShouldBe(DecisionKind.ApiCheck);
        session.Stage.ShouldBe(OnboardStage.Applicability);

        session = session.Decide(Applicability.Decide(proposals[1], DecisionKind.ApiCheck, "GET /v1/users.", "Alex Example", null, null, null).Value());
        session.Stage.ShouldBe(OnboardStage.Decided);
        session.Pending.ShouldBeEmpty();
    }

    [Fact]
    public void A_session_file_from_elsewhere_is_a_problem()
    {
        OnboardSessionJson.Read("""{"system":1}""").Problems().ShouldHaveSingleItem().Code.ShouldBe("onboard.session_unreadable");
    }
}
