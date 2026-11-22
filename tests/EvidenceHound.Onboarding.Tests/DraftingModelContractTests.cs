using System.Text.Json.Nodes;
using EvidenceHound.Onboarding.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Onboarding.Tests;

/// <summary>
/// The contract every model adapter keeps, run against each provider's recorded replies: it sends the instructions, the inputs, the schema
/// and the configured model; it returns the reply checked against the schema; and it maps a refusal, a reply that breaks the schema and an
/// unreachable provider to problems.
/// </summary>
public abstract class DraftingModelContractTests<TModel> : IDisposable
    where TModel : IDraftingModel
{
    internal static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("decision", "reason"),
        ["properties"] = new JsonObject
        {
            ["decision"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("api_check", "manual", "not_applicable", "other_system") },
            ["reason"] = new JsonObject { ["type"] = "string" },
        },
    };

    internal static readonly DraftRequest Request = new(
        "applicability",
        "Decide how the requirement is assessed for this system.",
        [new DraftInput("requirement", "Operating systems are supported."), new DraftInput("api", "GET /v1/orgs/{org}/devices lists devices with os.version.")],
        Schema);

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    protected WireMockServer Server { get; } = WireMockServer.Start();

    protected abstract string Provider { get; }

    protected abstract string Path { get; }

    protected abstract string Model { get; }

    protected abstract TModel Create(Uri baseUrl, string model);

    [Fact]
    public async Task Sends_the_schema_and_configured_model_and_returns_the_checked_reply()
    {
        Reply("reply");

        var reply = (await Create(new Uri(Server.Url!), Model).DraftAsync(Request, _ct)).Value();

        reply.Value["decision"]!.GetValue<string>().ShouldBe("api_check");
        (reply.InputTokens, reply.OutputTokens).ShouldBe((812, 64));
        var sent = JsonNode.Parse(Server.LogEntries.Single().RequestMessage!.Body!)!;
        sent["model"]!.GetValue<string>().ShouldBe(Model);
        var text = sent.ToJsonString();
        text.ShouldContain("other_system");
        text.ShouldContain("Decide how the requirement is assessed for this system.");
        text.ShouldContain("GET /v1/orgs/{org}/devices lists devices with os.version.");
    }

    [Fact]
    public async Task Maps_a_refusal_to_llm_refused()
    {
        Reply("refusal");

        (await Create(new Uri(Server.Url!), Model).DraftAsync(Request, _ct)).Problems().ShouldHaveSingleItem().Code.ShouldBe("llm.refused");
    }

    [Fact]
    public async Task Maps_a_reply_that_breaks_the_schema_to_llm_invalid_reply()
    {
        Reply("invalid");

        (await Create(new Uri(Server.Url!), Model).DraftAsync(Request, _ct)).Problems().ShouldHaveSingleItem().Code.ShouldBe("llm.invalid_reply");
    }

    [Fact]
    public async Task Maps_a_provider_error_to_llm_unreachable_without_the_key()
    {
        Server.Given(Post().WithPath(Path).UsingPost()).RespondWith(Response.Create().WithStatusCode(401).WithBody("{\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid key\"}}"));

        var problem = (await Create(new Uri(Server.Url!), Model).DraftAsync(Request, _ct)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("llm.unreachable");
        problem.Message.ShouldNotContain(ContractKeys.ApiKey);
    }

    public void Dispose()
    {
        Server.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IRequestBuilder Post() => WireMock.RequestBuilders.Request.Create();

    private void Reply(string name) =>
        Server.Given(Post().WithPath(Path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "files", "llm", Provider, $"{name}.json"))));
}

internal static class ContractKeys
{
    /// <summary>A made-up key: no test calls a real provider.</summary>
    public const string ApiKey = "example-test-llm-key";
}

public sealed class AnthropicDraftingModelContractTests : DraftingModelContractTests<AnthropicDraftingModel>
{
    protected override string Provider => "anthropic";

    protected override string Path => "/v1/messages";

    protected override string Model => "claude-opus-5-5";

    protected override AnthropicDraftingModel Create(Uri baseUrl, string model) =>
        new(new LlmSettings(LlmProvider.Anthropic, model, "high"), ContractKeys.ApiKey, baseUrl);
}

public sealed class OpenAiDraftingModelContractTests : DraftingModelContractTests<OpenAiDraftingModel>
{
    protected override string Provider => "openai";

    protected override string Path => "/v1/chat/completions";

    protected override string Model => "gpt-6.2-astra";

    protected override OpenAiDraftingModel Create(Uri baseUrl, string model) =>
        new(new LlmSettings(LlmProvider.OpenAi, model, "high"), ContractKeys.ApiKey, new Uri(baseUrl, "v1/"));
}
