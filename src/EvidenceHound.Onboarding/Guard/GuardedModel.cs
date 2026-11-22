using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Guard;

/// <summary>
/// The only way onboarding reaches a model. Each request is checked by the <see cref="OutboundGuard"/> (a hit sends and saves nothing), held to
/// the session's token budget, shown first with <c>--show-requests</c>, and saved with its reply in the session folder.
/// </summary>
public sealed class GuardedModel(IDraftingModel inner, GuardTerms terms, SessionRecord record, long budget, TextWriter? show) : IDraftingModel
{
    private long _used;
    private int _sent;

    public long TokensUsed => _used;

    public async Task<Result<DraftReply>> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var findings = OutboundGuard.Check(request, terms);
        if (findings.Count > 0)
        {
            return Result.Fail<DraftReply>(LlmProblems.Blocked(findings.Select(f => $"{f.What} in {f.Where}")));
        }

        if (_used >= budget)
        {
            return Result.Fail<DraftReply>(LlmProblems.BudgetReached(_used, budget));
        }

        var number = ++_sent;
        var sent = SessionRecord.Describe(request);
        if (show is not null)
        {
            await show.WriteLineAsync($"--- request {number} ({request.Step}) ---\n{sent.ToJsonString(SessionRecord.Indented)}".AsMemory(), cancellationToken);
            await show.FlushAsync(cancellationToken);
        }

        await record.SaveAsync(number, request.Step, "request", sent, cancellationToken);
        var reply = await inner.DraftAsync(request, cancellationToken);
        await record.SaveAsync(number, request.Step, "reply", reply.Match(
            r => new JsonObject { ["reply"] = r.Value.DeepClone(), ["input_tokens"] = r.InputTokens, ["output_tokens"] = r.OutputTokens },
            problems => new JsonObject { ["problems"] = new JsonArray([.. problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]) }), cancellationToken);
        if (reply is Result<DraftReply>.Ok { Value: var ok })
        {
            _used += ok.InputTokens + ok.OutputTokens;
        }

        return reply;
    }
}

/// <summary>The session's own record of what was sent and what came back, in its folder: <c>NNN-step.request.json</c> and <c>NNN-step.reply.json</c>.</summary>
public sealed class SessionRecord(string folder)
{
    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Folder => folder;

    public static JsonObject Describe(DraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["step"] = request.Step,
            ["instructions"] = request.Instructions,
            ["inputs"] = new JsonArray([.. request.Inputs.Select(i => new JsonObject { ["name"] = i.Name, ["content"] = i.Content })]),
            ["schema"] = request.Schema.DeepClone(),
        };
    }

    /// <summary>Lists the skills the session used, with source, version and SHA-256, in <c>skills.json</c>, so an auditor can see what guided it.</summary>
    public async Task SaveSkillsAsync(IEnumerable<Skills.Skill> skills, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Directory.CreateDirectory(folder);
        var list = new JsonArray([.. skills.Select(s => new JsonObject
        {
            ["name"] = s.Name,
            ["source"] = s.Source == Skills.SkillSource.Default ? "default" : "organisation",
            ["version"] = s.Version,
            ["sha256"] = s.Sha256,
            ["overrides"] = s.Overrides,
        })]);
        await File.WriteAllTextAsync(Path.Combine(folder, "skills.json"), list.ToJsonString(Indented), cancellationToken);
    }

    public async Task SaveAsync(int number, string step, string kind, JsonObject content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{number:000}-{step}.{kind}.json"));
        await File.WriteAllTextAsync(path, content.ToJsonString(Indented), cancellationToken);
    }
}
