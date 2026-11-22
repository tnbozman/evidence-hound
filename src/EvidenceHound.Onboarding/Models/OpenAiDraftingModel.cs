using System.ClientModel;
using EvidenceHound.Core.Results;
using OpenAI;
using OpenAI.Chat;

namespace EvidenceHound.Onboarding.Models;

/// <summary>A model through OpenAI's official .NET SDK, with the reply constrained to the step's schema (structured outputs, strict).</summary>
public sealed class OpenAiDraftingModel(LlmSettings settings, string apiKey, Uri? baseUrl = null) : IDraftingModel
{
    internal const int MaxReplyTokens = 16000;

    private readonly ChatClient _client = new(
        settings.Model,
        new ApiKeyCredential(apiKey),
        baseUrl is null ? new OpenAIClientOptions() : new OpenAIClientOptions { Endpoint = baseUrl });

    public async Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = MaxReplyTokens,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat($"{Slug(request.Step)}_reply", BinaryData.FromString(request.Schema.ToJsonString()), jsonSchemaIsStrict: true),
        };
#pragma warning disable OPENAI001 // Reasoning effort is marked for evaluation in the SDK; it is how llm.effort reaches an OpenAI reasoning model.
        if (Effort(settings.Effort) is { } effort)
        {
            options.ReasoningEffortLevel = effort;
        }
#pragma warning restore OPENAI001

        ChatCompletion completion;
        try
        {
            completion = (await _client.CompleteChatAsync([new SystemChatMessage(request.Instructions), new UserChatMessage(ReplyCheck.Material(request))], options, cancellationToken)).Value;
        }
        catch (ClientResultException exception)
        {
            return Result.Fail<DraftReply>(LlmProblems.Unreachable("OpenAI", exception.Status > 0 ? $"the provider answered {exception.Status}" : "no answer"));
        }
        catch (HttpRequestException exception)
        {
            return Result.Fail<DraftReply>(LlmProblems.Unreachable("OpenAI", exception.Message));
        }

        if (!string.IsNullOrEmpty(completion.Refusal))
        {
            return Result.Fail<DraftReply>(LlmProblems.Refused(completion.Refusal));
        }

        if (completion.FinishReason == ChatFinishReason.Length)
        {
            return Result.Fail<DraftReply>(LlmProblems.InvalidReply("the reply was cut off at the token limit"));
        }

        var text = string.Concat(completion.Content.Select(c => c.Text));
        return ReplyCheck.Check(text, request.Schema)
            .Map(value => new DraftReply(value, completion.Usage?.InputTokenCount ?? 0, completion.Usage?.OutputTokenCount ?? 0));
    }

#pragma warning disable OPENAI001 // See above.
    private static ChatReasoningEffortLevel? Effort(string? effort) => effort switch
    {
        "low" => ChatReasoningEffortLevel.Low,
        "medium" => ChatReasoningEffortLevel.Medium,
        "high" => ChatReasoningEffortLevel.High,
        _ => null,
    };
#pragma warning restore OPENAI001

    private static string Slug(string step) => new([.. step.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')]);
}
