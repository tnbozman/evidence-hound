using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Profiles;

/// <summary>An accepted API check: the requirement's key and title, and the reasoning that named the endpoint.</summary>
public sealed record ApiCheck(string Key, string Title, string Reasoning);

/// <summary>What the model is told to draft from: the system, the profile's name, the API summary, the accepted checks and response shapes.</summary>
public sealed record ProfileContext(SystemFacts System, string ProfileName, string Api, IReadOnlyList<ApiCheck> Checks, IReadOnlyList<DraftInput> Shapes);

/// <summary>The last draft, how many rounds it took, and the problems still left in it (none when it is clean).</summary>
public sealed record DraftOutcome(string Yaml, int Rounds, IReadOnlyList<Problem> Remaining);

/// <summary>
/// Drafting a profile: the model writes the profile for the accepted API checks; hound validates and tests it and sends the problems back,
/// for at most a few rounds. The draft is never saved as working until it passes, and a person reviews it before anything runs on a schedule.
/// </summary>
public static class ProfileDraft
{
    public const string Step = "profile";
    public const int DefaultRounds = 3;

    internal const string Instructions = """
        You write an Evidence Hound REST profile named in the system input: YAML with profile, version, description, parameters, secrets, connection (base_url,
        allowed_hosts, auth), queries, datasets and checks. Write one check for every check in the checks input, with a framework-neutral check ID
        of the form <profile>.<area>.<what>, and read only the endpoints the API summary names. Queries only read (GET, or POST only where the API
        reads with POST). Mark fields that identify people or devices under identities. Never put a secret, host name of the organisation or
        person's name in the profile: use parameters and secrets. Reply with the whole profile in profile_yaml and short notes. When problems are
        given, fix each of them in the previous draft and change nothing else.
        """;

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("profile_yaml", "notes"),
        ["properties"] = new JsonObject
        {
            ["profile_yaml"] = new JsonObject { ["type"] = "string" },
            ["notes"] = new JsonObject { ["type"] = "string" },
        },
    };

    /// <summary>The profile text from a reply; a reply without it, or with an empty one, is invalid.</summary>
    public static Result<string> Read(JsonNode reply) =>
        reply?["profile_yaml"] is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { } yaml && !string.IsNullOrWhiteSpace(yaml)
            ? Result.Ok(yaml)
            : Result.Fail<string>(LlmProblems.InvalidReply("profile_yaml must hold the profile as YAML text"));

    /// <summary>One input per sample answer, holding only its shape (field names, types and counts); identity fields never carry values.</summary>
    public static IReadOnlyList<DraftInput> ShapeInputs(IEnumerable<(string Name, JsonNode? Body)> answers, IReadOnlyList<string> identities)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return [.. answers.Where(a => a.Body is not null).Select(a => new DraftInput($"shape {a.Name}", Guard.ResponseShape.Of(a.Body, identities).ToJsonString()))];
    }

    public static DraftRequest Request(IReadOnlyList<Skill> skills, ProfileContext context, string? previous, IReadOnlyList<Problem> problems)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problems);
        List<DraftInput> inputs =
        [
            new("system", $"System: {context.System.Name}\nProduct: {context.System.Product}\nProfile name: {context.ProfileName}"),
            new("api", context.Api.Length > 0 ? context.Api : "No API specification was given."),
            new("checks", string.Join('\n', context.Checks.Select(c => $"[{c.Key}] {c.Title} ({c.Reasoning})"))),
            .. context.Shapes,
        ];
        if (previous is not null)
        {
            inputs.Add(new("previous draft", previous));
            inputs.Add(new("problems", string.Join('\n', problems.Select(p => $"{p.Code}: {p.Message}"))));
        }

        return StepRequest.Compose(Instructions, skills, inputs, Schema(), Step);
    }

    /// <summary>
    /// Drafts, checks with <paramref name="check"/> (validate, then test), and sends the problems back until the draft is clean or
    /// <paramref name="maxRounds"/> drafts were made. An invalid reply stops the loop.
    /// </summary>
    public static async Task<Result<DraftOutcome>> RunAsync(
        IDraftingModel model,
        IReadOnlyList<Skill> skills,
        ProfileContext context,
        Func<string, CancellationToken, Task<IReadOnlyList<Problem>>> check,
        int maxRounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(check);
        string? previous = null;
        IReadOnlyList<Problem> problems = [];
        for (var round = 1; round <= Math.Max(1, maxRounds); round++)
        {
            var drafted = (await model.DraftAsync(Request(skills, context, previous, problems), cancellationToken)).Bind(reply => Read(reply.Value));
            if (drafted is not Result<string>.Ok { Value: var yaml })
            {
                return Result.Fail<DraftOutcome>(((Result<string>.Fail)drafted).Errors);
            }

            problems = await check(yaml, cancellationToken);
            previous = yaml;
            if (problems.Count == 0 || round == Math.Max(1, maxRounds))
            {
                return Result.Ok(new DraftOutcome(yaml, round, problems));
            }
        }

        return Result.Ok(new DraftOutcome(previous ?? "", 0, problems));
    }
}
