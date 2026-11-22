using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Models;

/// <summary>
/// Claude through Anthropic's official .NET SDK: the reply constrained by structured outputs to the step's schema, with adaptive thinking at the
/// configured effort. The key is held by the SDK client only and never logged.
/// </summary>
public sealed class AnthropicDraftingModel(LlmSettings settings, string apiKey, Uri? baseUrl = null) : IDraftingModel
{
    internal const int MaxReplyTokens = 16000;

    private readonly AnthropicClient _client = baseUrl is null
        ? new AnthropicClient { ApiKey = apiKey }
        : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl.ToString().TrimEnd('/') };

    public async Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = MaxReplyTokens,
            System = request.Instructions,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig
            {
                Effort = Effort(settings.Effort),
                Format = new JsonOutputFormat
                {
                    Schema = request.Schema.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value), StringComparer.Ordinal),
                },
            },
            Messages = [new UserMessageParam { Content = ReplyCheck.Material(request) }],
        };

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicApiException exception)
        {
            return Result.Fail<DraftReply>(LlmProblems.Unreachable("Anthropic", Why(exception)));
        }
        catch (HttpRequestException exception)
        {
            return Result.Fail<DraftReply>(LlmProblems.Unreachable("Anthropic", exception.Message));
        }

        if (response.StopReason == "refusal")
        {
            return Result.Fail<DraftReply>(LlmProblems.Refused(response.StopDetails?.Explanation));
        }

        if (response.StopReason == "max_tokens")
        {
            return Result.Fail<DraftReply>(LlmProblems.InvalidReply("the reply was cut off at the token limit"));
        }

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return ReplyCheck.Check(text, request.Schema)
            .Map(value => new DraftReply(value, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens));
    }

    private static Effort? Effort(string? effort) => effort switch
    {
        "low" => Anthropic.Models.Messages.Effort.Low,
        "medium" => Anthropic.Models.Messages.Effort.Medium,
        "high" => Anthropic.Models.Messages.Effort.High,
        "max" => Anthropic.Models.Messages.Effort.Max,
        _ => null,
    };

    /// <summary>The status, without the response body, which could echo part of the request.</summary>
    private static string Why(AnthropicApiException exception) =>
        exception is Anthropic4xxException { StatusCode: var status } ? $"the provider answered {(int)status}" : exception.GetType().Name;
}
