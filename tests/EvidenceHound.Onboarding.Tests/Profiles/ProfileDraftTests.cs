using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Profiles;
using EvidenceHound.Onboarding.Tests.Journey;

namespace EvidenceHound.Onboarding.Tests.Profiles;

/// <summary>s09-23: the model drafts the profile for the accepted API checks as a structured reply; a reply that does not match is refused.</summary>
public sealed class ProfileDraftTests
{
    internal static readonly ProfileContext Context = new(
        new SystemFacts("EXAMPLE-ASSETS", "Example Asset API", "it@example.com"),
        "example-assets",
        "GET v1/orgs/{org}/devices: devices with os.version and last_seen; paging next_link.",
        [new ApiCheck("EX-1", "Operating systems are supported.", "Devices list os.version.")],
        [new DraftInput("shape devices", """{"items": [{"os": {"version": "string"}}]}""")]);

    [Theory]
    [InlineData("""{"notes": "no profile"}""")]
    [InlineData("""{"profile_yaml": "   ", "notes": ""}""")]
    [InlineData("""{"profile_yaml": 3, "notes": ""}""")]
    public void Reply_not_matching_schema_is_a_problem(string reply)
    {
        ProfileDraft.Read(JsonNode.Parse(reply)!).ShouldBeOfType<Result<string>.Fail>().Errors.ShouldHaveSingleItem().Code.ShouldBe("llm.invalid_reply");
    }

    [Fact]
    public void A_reply_gives_the_profile_text()
    {
        ProfileDraft.Read(JsonNode.Parse("""{"profile_yaml": "profile: example-assets\n", "notes": "Paging by next link."}""")!)
            .ShouldBeOfType<Result<string>.Ok>().Value.ShouldBe("profile: example-assets\n");
    }

    [Fact]
    public void The_request_names_the_checks_and_sends_shapes_never_responses()
    {
        var request = ProfileDraft.Request([], Context, previous: null, problems: []);

        request.Step.ShouldBe("profile");
        request.Inputs.Select(i => i.Name).ShouldBe(["system", "api", "checks", "shape devices"]);
        request.Inputs[2].Content.ShouldBe("[EX-1] Operating systems are supported. (Devices list os.version.)");
        request.Instructions.ShouldContain("example-assets");
        request.Schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["profile_yaml", "notes"]);
    }

    [Fact]
    public void A_later_round_sends_the_previous_draft_and_its_problems()
    {
        var request = ProfileDraft.Request([], Context, previous: "profile: example-assets\n", problems: [new Problem("profile.query_unknown", "check uses query 'devcies'")]);

        request.Inputs.Select(i => i.Name).ShouldContain("previous draft");
        request.Inputs.Single(i => i.Name == "problems").Content.ShouldBe("profile.query_unknown: check uses query 'devcies'");
    }
}
