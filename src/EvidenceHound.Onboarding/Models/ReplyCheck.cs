using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using Json.Schema;

namespace EvidenceHound.Onboarding.Models;

/// <summary>Checks a reply's text against the request's schema. Pure.</summary>
public static class ReplyCheck
{
    public static Result<JsonNode> Check(string? text, JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Fail<JsonNode>(LlmProblems.InvalidReply("the reply is empty"));
        }

        JsonNode? reply;
        try
        {
            reply = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return Result.Fail<JsonNode>(LlmProblems.InvalidReply("the reply is not JSON"));
        }

        if (reply is null)
        {
            return Result.Fail<JsonNode>(LlmProblems.InvalidReply("the reply is null"));
        }

        var result = JsonSchema.FromText(schema.ToJsonString()).Evaluate(JsonSerializer.SerializeToElement(reply), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return Result.Ok(reply);
        }

        var errors = (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")).Take(3);
        return Result.Fail<JsonNode>(LlmProblems.InvalidReply(string.Join("; ", errors)));
    }

    /// <summary>What a request with no inputs sends instead, because providers refuse an empty user message.</summary>
    public const string NoMaterial = "There is no material for this step.";

    /// <summary>
    /// The material as one message: each input wrapped in a tag that names it, so it is read as material, not as instructions. Never empty:
    /// a request with no inputs (such as <c>hound onboard config --check</c>) sends <see cref="NoMaterial"/>.
    /// </summary>
    public static string Material(DraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Inputs.Count == 0
            ? NoMaterial
            : string.Join("\n\n", request.Inputs.Select(i => $"<input name=\"{i.Name}\">\n{i.Content}\n</input>"));
    }
}
