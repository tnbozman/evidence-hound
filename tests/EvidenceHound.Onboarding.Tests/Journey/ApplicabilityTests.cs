using EvidenceHound.Core.Frameworks;
using EvidenceHound.Onboarding.Decisions;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>One empty proposal per in-scope requirement; a person decides each (or an agent drafts it), and not applicable needs a justification and an approver.</summary>
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

        Applicability.Empty(requirements).Select(p => p.Kind).ShouldBe([null, null]);
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
}
