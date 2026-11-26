using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound audit draft</c>: an audit for named systems, every answer an option, checked against the scope. The draft waits under
/// <c>onboarding/drafts/audits/</c>; a person approves it with <c>hound approve</c>, which moves it into <c>scope/audits/</c>.
/// </summary>
internal static class AuditCommands
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.audit");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var refId = new Option<string>("--ref") { Description = "The audit's ref_id, such as EX-E8-2027.", Required = true };
        var name = new Option<string>("--name") { Description = "The audit's name.", Required = true };
        var framework = new Option<string>("--framework") { Description = "The framework's URN, one that scope/setup.yaml loads.", Required = true };
        var groups = new Option<string[]>("--group") { Description = "An implementation group in scope, such as 2 for maturity level two. Repeat for more; none means every requirement.", AllowMultipleArgumentsPerToken = false };
        var domain = new Option<string?>("--domain") { Description = "The domain the audit belongs to. Default: the only domain." };
        var perimeter = new Option<string?>("--perimeter") { Description = "The perimeter the audit covers. Default: the domain's only perimeter." };
        var systems = new Option<string[]>("--system") { Description = "A system the audit covers, by name. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var due = new Option<string?>("--due") { Description = "The audit's due date, yyyy-MM-dd." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var draft = new Command("draft", "Draft an audit for named systems. A person approves it with 'hound approve', which moves it into scope/audits/.")
        {
            repo, refId, name, framework, groups, domain, perimeter, systems, due, json,
        };
        draft.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var asked = new Asked(
                parseResult.GetRequiredValue(refId), parseResult.GetRequiredValue(name), parseResult.GetRequiredValue(framework), parseResult.GetValue(groups) ?? [],
                parseResult.GetValue(domain), parseResult.GetValue(perimeter), parseResult.GetValue(systems) ?? [], parseResult.GetValue(due));
            var written = await DraftAsync(root, asked, AuditSeries.Today(configuration), cancellationToken);
            return await written.Match(
                async file =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json)
                        ? new JsonObject { ["file"] = file, ["audit"] = asked.RefId, ["drafted_by"] = OnboardAgentCommand.DraftedBy, ["approved"] = false }.ToJsonString(CommandJson.Readable)
                        : $"Drafted {file}. A person approves {asked.RefId} with 'hound approve', which moves it into scope/audits/.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return new Command("audit", "Work with audits in a compliance repo.") { draft };
    }

    /// <summary>The first line of a drafted audit, and of the audit once a person approved it.</summary>
    public static string Header(DateOnly drafted, string? approver, DateOnly? approved) => approver is null
        ? string.Create(CultureInfo.InvariantCulture, $"# Drafted by an agent with hound audit draft on {drafted:yyyy-MM-dd}. A person approves it with hound approve, which moves it into scope/audits/.")
        : string.Create(CultureInfo.InvariantCulture, $"# Drafted by an agent with hound audit draft on {drafted:yyyy-MM-dd}. Approved by {approver} on {approved:yyyy-MM-dd}.");

    private static async Task<Result<string>> DraftAsync(string repo, Asked asked, DateOnly today, CancellationToken cancellationToken)
    {
        var parsed = await OnboardEnvironmentCommand.ParseAsync(Path.Combine(repo, "scope"), cancellationToken);
        if (parsed.Problems.Count > 0)
        {
            return Result.Fail<string>([.. parsed.Problems.Select(p => new Problem(p.Code, $"{p.At.File} line {p.At.Line}: {p.Message}"))]);
        }

        var setup = parsed.Scope.Setup;
        var problems = new List<Problem>();
        var relative = Drafts.AuditFile(asked.RefId);
        if (parsed.Scope.Audits.Any(a => string.Equals(a.RefId, asked.RefId, StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(repo, relative)))
        {
            problems.Add(new Problem("audit.exists", $"{asked.RefId} already exists, in scope/audits/ or as a draft. Choose another ref_id, or edit that file."));
        }

        if (!RefIds.Follows(RefIdKind.Audit, asked.RefId))
        {
            problems.Add(new Problem("audit.ref_id_invalid", $"'{asked.RefId}' is not an audit's ref ID. {RefIds.Rule(RefIdKind.Audit)}"));
        }

        if (!setup.Frameworks.Any(f => f.Urn == asked.Framework))
        {
            problems.Add(new Problem("audit.framework_unknown", $"'{asked.Framework}' is not a framework scope/setup.yaml loads. Known: {string.Join(", ", setup.Frameworks.Select(f => f.Urn))}."));
        }

        var domain = asked.Domain is null ? (setup.Domains is [var only] ? only : null) : setup.Domains.FirstOrDefault(d => d.Name == asked.Domain);
        if (domain is null)
        {
            problems.Add(new Problem("audit.domain_unknown", asked.Domain is null
                ? $"Name the audit's domain with --domain: {string.Join(", ", setup.Domains.Select(d => d.Name))}."
                : $"'{asked.Domain}' is not a domain of scope/setup.yaml. Known: {string.Join(", ", setup.Domains.Select(d => d.Name))}."));
        }

        var perimeter = domain is null ? null : asked.Perimeter is null ? (domain.Perimeters is [var one] ? one : null) : domain.Perimeters.FirstOrDefault(p => p.Name == asked.Perimeter);
        if (domain is not null && perimeter is null)
        {
            problems.Add(new Problem("audit.perimeter_unknown", asked.Perimeter is null
                ? $"Name the audit's perimeter with --perimeter: {string.Join(", ", domain.Perimeters.Select(p => p.Name))}."
                : $"'{asked.Perimeter}' is not a perimeter of {domain.Name}. Known: {string.Join(", ", domain.Perimeters.Select(p => p.Name))}."));
        }

        var known = setup.Domains.SelectMany(d => d.Systems).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(asked.Systems.Where(s => !known.Contains(s)).Select(s => new Problem(
            "audit.system_unknown", $"'{s}' is not a system of scope/setup.yaml. Onboard it first, or name one of: {string.Join(", ", known.Order(StringComparer.Ordinal))}.")));
        if (asked.Systems.Count == 0)
        {
            problems.Add(new Problem("audit.systems_missing", "Name each system the audit covers with --system."));
        }

        if (asked.Due is { } date && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            problems.Add(new Problem("audit.due_invalid", $"'{date}' is not a date; write --due as yyyy-MM-dd."));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<string>(problems);
        }

        static string Quote(string value) => JsonSerializer.Serialize(value);
        static string List(IEnumerable<string> values) => $"[{string.Join(", ", values.Select(Quote))}]";
        var text = new StringBuilder()
            .Append(Header(today, null, null)).Append('\n')
            .Append(CultureInfo.InvariantCulture, $"ref_id: {Quote(asked.RefId)}\nname: {Quote(asked.Name)}\nframework: {Quote(asked.Framework)}\n");
        if (asked.Groups.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"groups: {List(asked.Groups)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"domain: {Quote(domain!.Name)}\nperimeter: {Quote(perimeter!.Name)}\n");
        if (asked.Due is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"due_date: {Quote(asked.Due)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"systems: {List(asked.Systems)}\n").Append("unlisted:\n  result: not_assessed\n");
        var file = Path.Combine(repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text.ToString(), cancellationToken);
        return Result.Ok(relative);
    }

    private sealed record Asked(string RefId, string Name, string Framework, IReadOnlyList<string> Groups, string? Domain, string? Perimeter, IReadOnlyList<string> Systems, string? Due);
}
