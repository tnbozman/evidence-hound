using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Capabilities;
using EvidenceHound.Onboarding.Skills;
using EvidenceHound.Onboarding.Tests.Journey;

namespace EvidenceHound.Onboarding.Tests.Capabilities;

/// <summary>s09-28: after the signals, the model proposes which system provides each mitigation, with its reasoning; still never a decision.</summary>
public sealed class CapabilityProposalsTests
{
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    private static readonly Mitigation[] Mitigations =
    [
        new("ac", [Node("ac", "ISM-0843", "Application control is implemented on workstations.")]),
        new("mfa", [Node("mfa", "ISM-1504", "Multi-factor authentication is used to authenticate users.")]),
        new("rb", [Node("rb", "ISM-1511", "Backups of data are performed.")]),
    ];

    private static readonly ScopeSystem[] Systems =
    [
        new("Example 365", null, SystemType.Primary, "Mail, files, identity and device management.", null, [], new Dictionary<string, string>(), At),
        new("Example backup", null, SystemType.Primary, "Backups of mail and files.", null, [], new Dictionary<string, string>(), At),
    ];

    private static readonly EnvironmentFacts Facts = new("Microsoft Graph (recorded 2026-11-23)", ["AAD_PREMIUM_P2"], []);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_model_proposes_after_the_signals_and_its_reasoning_is_kept()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply(
            ("mfa", "system", ["Example 365"], "Entra ID P2 is subscribed and Example 365 holds identity."),
            ("rb", "system", ["Example backup"], "Example backup backs up mail and files."),
            ("ac", "gap", [], "No system in scope enforces application control.")));
        var prefilled = CapabilitySignals.Prefill(Mitigations, Systems, Facts, "Example 365");

        var proposals = (await CapabilityProposals.ProposeAsync(model, [Guidance], Mitigations, Systems, prefilled, Token)).Value();

        var request = model.Sent.ShouldHaveSingleItem();
        request.Step.ShouldBe("environment");
        request.Instructions.ShouldContain("<skill name=\"environment-capabilities\"");
        request.Inputs.Select(i => i.Name).ShouldBe(["mitigations", "systems", "signals"]);
        request.Inputs[0].Content.ShouldContain("[ac] 1 requirement(s), such as ISM-0843: Application control is implemented on workstations.");
        request.Inputs[1].Content.ShouldContain("Example backup (primary): Backups of mail and files.");
        request.Inputs[2].Content.ShouldContain("[mfa] Microsoft Graph (recorded 2026-11-23): licence AAD_PREMIUM_P2");

        var rb = proposals.Single(p => p.Mitigation == "rb");
        (rb.Suggested, rb.Systems.Single(), rb.Reasoning).ShouldBe((CapabilityProvision.System, "Example backup", "Example backup backs up mail and files."));
        var ac = proposals.Single(p => p.Mitigation == "ac");
        ac.Suggested.ShouldBe(CapabilityProvision.Gap);
        proposals.Single(p => p.Mitigation == "mfa").Signals.ShouldHaveSingleItem().ShouldContain("AAD_PREMIUM_P2");

        // Accepting records that the suggestion came from the model, with its reasoning; the person still decides.
        var decided = CapabilityAnswers.Decide(rb, CapabilityAnswer.Accept, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value();
        decided.Reason.ShouldBe("Accepted by Alex Example from the model's proposal: Example backup backs up mail and files.");
        CapabilityAnswers.Decide(ac, CapabilityAnswer.Accept, "Alex Example", "GRC lead", new DateOnly(2026, 11, 23)).Value().ProvidedBy.ShouldBe(CapabilityProvision.Gap);
    }

    [Fact]
    public async Task A_mitigation_the_model_leaves_out_keeps_its_signals_suggestion()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply(("rb", "manual", [], "No backup product is named.")));
        var prefilled = CapabilitySignals.Prefill(Mitigations, Systems, Facts, "Example 365");

        var proposals = (await CapabilityProposals.ProposeAsync(model, [], Mitigations, Systems, prefilled, Token)).Value();

        var mfa = proposals.Single(p => p.Mitigation == "mfa");
        (mfa.Suggested, mfa.Systems.Single(), mfa.Reasoning).ShouldBe((CapabilityProvision.System, "Example 365", null));
        proposals.Single(p => p.Mitigation == "rb").Suggested.ShouldBe(CapabilityProvision.Manual);
    }

    [Theory]
    [InlineData("pa")]
    [InlineData("rb,rb")]
    public async Task A_reply_about_mitigations_not_asked_or_twice_is_invalid(string codes)
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply([.. codes.Split(',').Select(c => (c, "manual", Array.Empty<string>(), "Because."))]));

        var problem = (await CapabilityProposals.ProposeAsync(model, [], Mitigations, Systems, CapabilitySignals.Prefill(Mitigations, Systems, EnvironmentFacts.None, null), Token))
            .ShouldBeOfType<Result<IReadOnlyList<CapabilityProposal>>.Fail>().Errors.ShouldHaveSingleItem();

        problem.Code.ShouldBe("llm.invalid_reply");
    }

    [Fact]
    public async Task A_system_proposal_without_systems_is_no_suggestion()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply(("ac", "system", [], "Something enforces it.")));

        var proposals = (await CapabilityProposals.ProposeAsync(model, [], Mitigations, Systems, CapabilitySignals.Prefill(Mitigations, Systems, EnvironmentFacts.None, null), Token)).Value();

        proposals.Single(p => p.Mitigation == "ac").Suggested.ShouldBeNull();
    }

    private static Skill Guidance => SkillCatalogue.Defaults().Value().Single(s => s.Name == "environment-capabilities");

    private static JsonObject Reply(params (string Code, string Kind, string[] Systems, string Reasoning)[] items) => new()
    {
        ["mitigations"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
        {
            ["mitigation"] = i.Code,
            ["provided_by"] = i.Kind,
            ["systems"] = new JsonArray([.. i.Systems.Select(s => (JsonNode)JsonValue.Create(s))]),
            ["reasoning"] = i.Reasoning,
        })]),
    };

    private static SnapshotNode Node(string mitigation, string refId, string title) =>
        new($"urn:example:{mitigation}:{refId}", refId, $"urn:example:{mitigation}", ["1", "2"], title);
}
