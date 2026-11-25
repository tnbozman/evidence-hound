using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Setup;

/// <summary><c>hound init</c>: asks about a new organisation and writes its compliance repo, which validates as written.</summary>
internal static class InitCommand
{
    private static readonly string DefaultName = "Example Pty Ltd";
    private static readonly string DefaultUrl = "https://ciso.example.com/api/";
    private static readonly string DefaultTimeZone = "Australia/Sydney";
    private static readonly string DefaultOwner = "ceo@example.com";
    private static readonly string DefaultEvidenceRoot = "/var/lib/evidence-hound/evidence";
    private static readonly string DefaultInterval = "quarter";

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.init");
        var folder = new Argument<DirectoryInfo>("folder") { Description = "The new compliance repo's folder: missing or empty." };
        var org = new Option<string?>("--org") { Description = $"The organisation's name, also its domain in CISO Assistant. Default: {DefaultName}." };
        var code = new Option<string?>("--code") { Description = "A short code for ref IDs, such as EX in EX-E8-2026. Default: from the name." };
        var url = new Option<string?>("--url") { Description = $"The CISO Assistant API address. Default: {DefaultUrl}" };
        var timeZone = new Option<string?>("--time-zone") { Description = $"The IANA time zone schedules run in. Default: {DefaultTimeZone}." };
        var level = new Option<string?>("--level") { Description = "The Essential Eight maturity level to assess, 1 to 3. Default: 2." };
        var interval = new Option<string?>("--interval") { Description = "How the audit runs: quarter (each quarter of the financial year, July to June) or month. Default: quarter." };
        var due = new Option<string?>("--due") { Hidden = true, Description = "No longer used: the audit runs in periods, each due on its last day." };
        var owner = new Option<string?>("--owner") { Description = $"The email of whoever owns compliance. Default: {DefaultOwner}." };
        var evidenceRoot = new Option<string>("--evidence-root") { Description = "Where the runner keeps evidence, outside the repo.", DefaultValueFactory = _ => DefaultEvidenceRoot };
        var frameworks = new Option<string?>("--framework") { Description = "Also write an audit beside the Essential Eight one, comma separated: disp (DISP membership readiness). Default: none." };
        var assistant = new Option<string?>("--assistant") { Description = "Also set up a coding assistant in the repo, as 'hound agent setup' does: claude, copilot, both or none. Default: none." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Ask nothing: take the options given and the defaults." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent: the organisation, the audit and every file written." };

        var init = new Command("init", "Write a new organisation's compliance repo: config, scope, an Essential Eight audit and its mapping. Asks each question it needs.")
        {
            folder, org, code, url, timeZone, level, interval, due, owner, evidenceRoot, frameworks, assistant, yes, json,
        };
        init.SetAction(async (parseResult, cancellationToken) =>
        {
            var target = parseResult.GetRequiredValue(folder);
            if (target.Exists && target.EnumerateFileSystemInfos().Any())
            {
                return Log.Problems(logger, [new Problem("init.folder_not_empty", $"{target.FullName} is not empty. hound init writes a new repo; give it a new or empty folder.")]);
            }

            if (parseResult.GetValue(due) is not null)
            {
                await terminal.Error.WriteLineAsync("hound init: --due is no longer used; the audit runs by quarter or month, and each period is due on its last day.".AsMemory(), cancellationToken);
            }

            var wizard = new Wizard(terminal, parseResult.GetValue(yes));
            var name = await wizard.AskAsync("Organisation name", parseResult.GetValue(org), DefaultName, cancellationToken);
            var answers = new Answers(
                name,
                await wizard.AskAsync("Short code for ref IDs", parseResult.GetValue(code), DefaultCode(name), cancellationToken),
                await wizard.AskAsync("CISO Assistant API address", parseResult.GetValue(url), DefaultUrl, cancellationToken),
                await wizard.AskAsync("Time zone", parseResult.GetValue(timeZone), DefaultTimeZone, cancellationToken),
                await wizard.AskAsync("Essential Eight maturity level to assess (1, 2 or 3)", parseResult.GetValue(level), "2", cancellationToken),
                await wizard.AskAsync("Audit interval (quarter or month)", parseResult.GetValue(interval), DefaultInterval, cancellationToken),
                await wizard.AskAsync("Compliance owner's email, as a CISO Assistant user", parseResult.GetValue(owner), DefaultOwner, cancellationToken),
                parseResult.GetRequiredValue(evidenceRoot),
                AuditSeries.Today(configuration),
                parseResult.GetValue(frameworks) ?? "");

            // With --json the steps' own lines are held back, and one JSON answer says what was written.
            var asJson = parseResult.GetValue(json);
            using var held = new StringWriter();
            var steps = asJson ? terminal with { Output = held } : terminal;
            var checkedAnswers = Check(answers);
            var written = await checkedAnswers.Match(
                organisation => WriteAsync(target, organisation, steps, logger, registry, configuration, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
            if (written != ExitCodes.Ok)
            {
                return written;
            }

            // The offer at the end: the assistant files, as hound agent setup writes them.
            var wanted = await wizard.AskAsync(AssistantQuestion, parseResult.GetValue(assistant), "none", cancellationToken);
            var setUp = AgentCommands.Parse(wanted) is { } chosen ? await AgentCommands.SetupAsync(target.FullName, chosen, steps, logger, cancellationToken) : ExitCodes.Ok;
            if (asJson && setUp == ExitCodes.Ok)
            {
                var organisation = checkedAnswers.Match(o => o, _ => throw new InvalidOperationException());
                var files = Directory.GetFiles(target.FullName, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(target.FullName, f).Replace('\\', '/')).Order(StringComparer.Ordinal);
                await terminal.Output.WriteLineAsync(new JsonObject
                {
                    ["folder"] = target.FullName,
                    ["organisation"] = organisation.Name,
                    ["audit"] = organisation.AuditRef,
                    ["files"] = new JsonArray([.. files.Select(f => (JsonNode)f)]),
                }.ToJsonString(OnboardAgentCommand.Indented).AsMemory(), cancellationToken);
            }

            return setUp;
        });

        return init;
    }

    internal const string AssistantQuestion = "Set up an AI assistant in this repo (claude, copilot, both or none)";

    private sealed record Answers(string Name, string Code, string Url, string TimeZone, string Level, string Interval, string Owner, string EvidenceRoot, DateOnly Today, string Frameworks);

    private static Result<NewOrganisation> Check(Answers a)
    {
        var code = a.Code.ToUpperInvariant();
        var problems = new List<Problem>();
        if (a.Name.Length == 0)
        {
            problems.Add(new Problem("init.answer_invalid", "The organisation name is empty."));
        }

        if (code.Length is 0 or > 10 || !code.All(char.IsAsciiLetterOrDigit))
        {
            problems.Add(new Problem("init.answer_invalid", $"The short code '{a.Code}' must be 1 to 10 letters or digits."));
        }

        if (!Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            problems.Add(new Problem("init.answer_invalid", $"'{a.Url}' is not an http or https address."));
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(a.TimeZone, out _))
        {
            problems.Add(new Problem("init.answer_invalid", $"'{a.TimeZone}' is not a time zone this system knows, such as {DefaultTimeZone}."));
        }

        if (!int.TryParse(a.Level, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 1 or > 3)
        {
            problems.Add(new Problem("init.answer_invalid", $"The maturity level '{a.Level}' must be 1, 2 or 3."));
        }

        var interval = a.Interval.ToLowerInvariant();
        if (!AuditPeriods.Lengths.Contains(interval, StringComparer.Ordinal))
        {
            problems.Add(new Problem("init.answer_invalid", $"The audit interval '{a.Interval}' must be quarter or month."));
        }

        if (!a.Owner.Contains('@', StringComparison.Ordinal))
        {
            problems.Add(new Problem("init.answer_invalid", $"The owner '{a.Owner}' must be an email address."));
        }

        var ids = a.Frameworks.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(f => f.ToLowerInvariant()).Distinct().ToList();
        foreach (var unknown in ids.Where(id => ExtraFramework.All.All(e => e.Id != id)))
        {
            problems.Add(new Problem("init.answer_invalid", $"The framework '{unknown}' is not one hound init can add; it can add {string.Join(", ", ExtraFramework.All.Select(e => e.Id))}."));
        }

        return problems.Count > 0
            ? Result.Fail<NewOrganisation>([.. problems])
            : Result.Ok(new NewOrganisation(a.Name, code, a.Url, a.TimeZone, level, interval, FirstDay(interval, a.Today), a.Owner, a.EvidenceRoot)
            {
                Extras = [.. ExtraFramework.All.Where(e => ids.Contains(e.Id))],
            });
    }

    private static async Task<int> WriteAsync(
        DirectoryInfo target, NewOrganisation org, Terminal terminal, ILogger logger, CollectorRegistry registry, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var files = RepoTemplate.Files(org);
        foreach (var (relative, text) in files)
        {
            await WriteFileAsync(target, relative, text, cancellationToken);
        }

        var copied = RepoTemplate.CopiedFor(org);
        foreach (var relative in copied)
        {
            await WriteFileAsync(target, relative, await BundledAsync(relative, cancellationToken), cancellationToken);
        }

        var config = new FileInfo(Path.Combine(target.FullName, "hound.yaml"));
        var checkedConfig = await ConfigCommands.LoadAsync(config, registry, cancellationToken);
        var checkedScope = await ScopeCommands.ValidateAsync(new DirectoryInfo(Path.Combine(target.FullName, "scope")), config, registry, AuditSeries.Today(configuration), cancellationToken);
        Problem[] problems = [.. checkedConfig.Match(_ => [], p => p), .. checkedScope.Problems.Select(p => p.ToProblem())];
        if (problems.Length > 0)
        {
            return Log.Problems(logger, problems);
        }

        await terminal.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Wrote {org.Name}'s compliance repo to {target.FullName}: {files.Count + copied.Count} files, {(org.Extras.Count == 0 ? "audit" : "audits")} {string.Join(", ", [org.AuditRef, .. org.Extras.Select(org.ExtraAuditRef)])}."));
        await terminal.Output.WriteLineAsync("Next: make it a git repository (git init) and push it to a private repository; add systems, controls and collectors (see README.md), then hound scope plan and apply. hound agent setup writes the files a coding assistant needs to help.");
        return ExitCodes.Ok;
    }

    private static async Task WriteFileAsync(DirectoryInfo target, string relative, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(target.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken);
    }

    /// <summary>A file hound carries, from its embedded copy. A mapping's schema comment points into the Evidence Hound repo, so it is left out.</summary>
    private static async Task<string> BundledAsync(string relative, CancellationToken cancellationToken)
    {
        await using var stream = typeof(InitCommand).Assembly.GetManifestResourceStream($"init/{relative}")
            ?? throw new InvalidOperationException($"hound was built without its copy of {relative}.");
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.StartsWith("# yaml-language-server:", StringComparison.Ordinal) ? text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..] : text;
    }

    private static string DefaultCode(string name) =>
        Wizard.Slug(name).Replace("-", "", StringComparison.Ordinal) is { Length: > 0 } letters ? letters[..Math.Min(2, letters.Length)] : "EX";

    /// <summary>The first day of the period holding <paramref name="today"/>: its month, or its financial-year quarter (July, October, January, April).</summary>
    private static DateOnly FirstDay(string interval, DateOnly today) =>
        interval == "quarter"
            ? new DateOnly(today.Year, today.Month, 1).AddMonths(-((today.Month - AuditPeriods.FinancialYearStart + 12) % 3))
            : new DateOnly(today.Year, today.Month, 1);
}
