using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Models;

/// <summary>
/// The port to a language model, used only by <c>hound onboard</c>: instructions (skills and the step's own), the material to read, and the
/// JSON schema the reply must meet go in; a reply checked against that schema, or a problem, comes out.
/// </summary>
public interface IDraftingModel
{
    Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken);
}

/// <summary>One request: the onboarding step it serves, the instructions, the inputs (each named), and the schema of the reply.</summary>
public sealed record DraftRequest(string Step, string Instructions, IReadOnlyList<DraftInput> Inputs, JsonObject Schema);

/// <summary>Material for the model to read, such as an API specification or a response shape. Never instructions.</summary>
public sealed record DraftInput(string Name, string Content);

/// <summary>A reply that meets the request's schema, with the tokens it took.</summary>
public sealed record DraftReply(JsonNode Value, int InputTokens, int OutputTokens);

public enum LlmProvider
{
    Anthropic,
    OpenAi,
}

/// <summary>The provider, the model and the effort to ask for.</summary>
public sealed record LlmSettings(LlmProvider Provider, string Model, string? Effort);

/// <summary>The problems a model request ends in. None repeats the key.</summary>
public static class LlmProblems
{
    public static Problem Refused(string? why) =>
        new("llm.refused", $"The model declined the request{(string.IsNullOrWhiteSpace(why) ? "." : $": {why}")} Change the material or the step, or decide it yourself.");

    public static Problem InvalidReply(string why) =>
        new("llm.invalid_reply", $"The model's reply does not meet the step's schema: {why}. Nothing was accepted; run the step again or decide it yourself.");

    public static Problem BudgetReached(long used, long budget) =>
        new("llm.budget_reached", $"This session has used {used} of its {budget} tokens (llm.max_tokens_per_session in onboarding.yaml). Raise the budget or finish the step yourself.");

    public static Problem Unreachable(string provider, string why) =>
        new("llm.unreachable", $"Could not get a reply from {provider}: {why}. Check the key with 'hound onboard config --check' and the network.");

    public static Problem Blocked(IEnumerable<string> findings) =>
        new("llm.blocked_content", $"Nothing was sent: the request holds {string.Join("; ", findings)}. Remove it from the material, or add a scrubbed copy.");
}
