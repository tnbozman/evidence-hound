using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Tests;

/// <summary>Every request goes through the gate: the guard, the session's token budget, the local record and <c>--show-requests</c>.</summary>
public sealed class GuardedModelTests : IDisposable
{
    private readonly string _session = Directory.CreateTempSubdirectory("hound-session-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly FakeModel _inner = new();

    [Fact]
    public async Task Blocked_request_is_never_sent_or_saved()
    {
        var gate = new GuardedModel(_inner, new GuardTerms(new Dictionary<string, string>(), []), new SessionRecord(_session), 100_000, null);

        var problem = (await gate.DraftAsync(Request("Ask admin@corp.example.org."), _ct)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("llm.blocked_content");
        problem.Message.ShouldContain("an email address in api");
        _inner.Sent.ShouldBeEmpty();
        Directory.GetFiles(_session, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Requests_and_replies_are_saved_and_shown_before_sending()
    {
        using var shown = new StringWriter();
        var gate = new GuardedModel(_inner, new GuardTerms(new Dictionary<string, string>(), []), new SessionRecord(_session), 100_000, shown);

        (await gate.DraftAsync(Request("Devices list os.version."), _ct)).Value();

        shown.ToString().ShouldContain("Devices list os.version.");
        var files = Directory.GetFiles(_session, "*.json", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        files.ShouldBe(["001-applicability.reply.json", "001-applicability.request.json"]);
    }

    [Fact]
    public async Task Budget_reached_stops_the_next_request()
    {
        var gate = new GuardedModel(_inner, new GuardTerms(new Dictionary<string, string>(), []), new SessionRecord(_session), 100, null);

        (await gate.DraftAsync(Request("One."), _ct)).Value();
        (await gate.DraftAsync(Request("Two."), _ct)).Problems().ShouldHaveSingleItem().Code.ShouldBe("llm.budget_reached");
        _inner.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Numbering_continues_when_a_session_resumes()
    {
        var terms = new GuardTerms(new Dictionary<string, string>(), []);
        (await new GuardedModel(_inner, terms, new SessionRecord(_session), 100_000, null).DraftAsync(Request("One."), _ct)).Value();
        (await new GuardedModel(_inner, terms, new SessionRecord(_session), 100_000, null).DraftAsync(Request("Two."), _ct)).Value();

        Directory.GetFiles(_session, "*.request.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["001-applicability.request.json", "002-applicability.request.json"]);
    }

    public void Dispose() => Directory.Delete(_session, recursive: true);

    private static DraftRequest Request(string api) =>
        new("applicability", "Decide.", [new DraftInput("api", api)], DraftingModelContractTests<AnthropicDraftingModel>.Schema);

    private sealed class FakeModel : IDraftingModel
    {
        public List<DraftRequest> Sent { get; } = [];

        public Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return Task.FromResult(Result.Ok(new DraftReply(new JsonObject { ["decision"] = "manual", ["reason"] = "Not in the API." }, 80, 40)));
        }
    }
}
