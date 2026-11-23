using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;

namespace EvidenceHound.Onboarding.Output;

/// <summary>Where the system's audit sits in CISO Assistant: a domain and one of its perimeters from <c>scope/setup.yaml</c>.</summary>
public sealed record AuditPlace(string Domain, string Perimeter);

/// <summary>A skill that guided the session, as the session record lists it.</summary>
public sealed record SkillUse(string Name, string Source, string Version, string Sha256);

/// <summary>
/// What a decided session writes into the config repo's working copy, for review in a pull request: the system's own audit with the
/// rows decided by hand, manual rows added to the framework's mapping, and the onboarding record. Pure; the CLI writes the files.
/// </summary>
public static partial class OnboardOutput
{
    public static string AuditFileName(string system, string frameworkName) => $"{Slug(system)}-{Slug(frameworkName)}.yaml";

    public static string RecordFileName(string system, string frameworkName, DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"{Slug(system)}-{Slug(frameworkName)}-{today:yyyy-MM-dd}.md");

    public static string AuditRefId(string system, string frameworkName) => $"{system}-{frameworkName}".ToUpperInvariant();

    /// <summary>
    /// <c>scope/audits/&lt;system&gt;-&lt;framework&gt;.yaml</c>: one audit for the system. Not applicable and other-system rows are done with
    /// result <c>not_applicable</c>, their justification, approver or providing system in the observation; manual rows are to do. Rows
    /// checked through the API are left to the collector.
    /// </summary>
    public static string Audit(OnboardSession session, string frameworkUrn, string frameworkName, AuditPlace place, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(place);
        var record = $"onboarding/{RecordFileName(session.System, frameworkName, today)}";
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Written by hound onboard for {session.System} on {today:yyyy-MM-dd}; how each row was decided is in {record}.\n");
        text.Append(CultureInfo.InvariantCulture, $"ref_id: {Quote(AuditRefId(session.System, frameworkName))}\n");
        text.Append(CultureInfo.InvariantCulture, $"name: {Quote($"{session.System} {frameworkName}")}\n");
        text.Append(CultureInfo.InvariantCulture, $"framework: {Quote(frameworkUrn)}\n");
        if (session.Groups.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"groups: [{string.Join(", ", session.Groups.Select(Quote))}]\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"domain: {Quote(place.Domain)}\nperimeter: {Quote(place.Perimeter)}\nsystems: [{Quote(session.System)}]\n");
        text.Append("# Requirements not listed below are checked through the API; they stay not assessed until the collector's results are published.\n");
        text.Append("unlisted:\n  result: not_assessed\n");
        var accepted = string.Create(CultureInfo.InvariantCulture, $"when onboarding {session.System} on {today:yyyy-MM-dd} ({record}).");
        var rows = session.Decisions.Where(d => d.Kind != DecisionKind.ApiCheck).ToArray();
        if (rows.Length > 0)
        {
            text.Append("assessments:\n");
        }

        foreach (var decision in rows)
        {
            var (status, result, observation) = decision.Kind switch
            {
                DecisionKind.NotApplicable => ("done", "not_applicable", $"Not applicable: {decision.Justification} Approved by {decision.Approver}; accepted by {decision.AcceptedBy} {accepted}"),
                DecisionKind.OtherSystem => ("done", "not_applicable", $"Provided by {decision.OtherSystem}: {decision.Reasoning} Accepted by {decision.AcceptedBy} {accepted}"),
                _ => ("to_do", "not_assessed", $"Manual evidence: {decision.Reasoning} Accepted by {decision.AcceptedBy} {accepted}"),
            };
            text.Append(CultureInfo.InvariantCulture, $"  - {Key(decision)}\n    status: {status}\n    result: {result}\n    observation: {Quote(observation)}\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Adds each manual decision the mapping does not already name to its <c>manual_only</c> list, with the reasoning as the suggested source.
    /// Existing lines are never changed; a second run adds nothing.
    /// </summary>
    public static (string Text, IReadOnlyList<string> Added) Mapping(string mapping, OnboardSession session)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(session);
        var named = Named().Matches(mapping).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);
        var manual = session.Decisions.Where(d => d.Kind == DecisionKind.Manual && !named.Contains(d.Key) && !(d.RefId is { } r && named.Contains(r))).ToArray();
        if (manual.Length == 0)
        {
            return (mapping, []);
        }

        var block = new StringBuilder();
        block.Append(CultureInfo.InvariantCulture, $"  # {session.System}, added by hound onboard: manual evidence.\n");
        foreach (var decision in manual)
        {
            block.Append(CultureInfo.InvariantCulture, $"  - {Key(decision)}\n    source: {Quote(decision.Reasoning)}\n");
        }

        var lines = mapping.Split('\n').ToList();
        var start = lines.FindIndex(l => l.StartsWith("manual_only:", StringComparison.Ordinal));
        if (start < 0)
        {
            var ending = mapping.Length == 0 || mapping.EndsWith('\n') ? "" : "\n";
            return (mapping + ending + "manual_only:\n" + block, [.. manual.Select(d => d.Key)]);
        }

        var end = start + 1;
        while (end < lines.Count && (lines[end].Length == 0 || lines[end][0] is ' ' or '-'))
        {
            end++;
        }

        while (end > start + 1 && lines[end - 1].Length == 0)
        {
            end--;
        }

        lines.Insert(end, block.ToString().TrimEnd('\n'));
        return (string.Join('\n', lines), [.. manual.Select(d => d.Key)]);
    }

    /// <summary>The onboarding record: who decided what and how, for the pull request and for auditors.</summary>
    public static string Record(OnboardSession session, IReadOnlyList<SkillUse> skills, string frameworkName, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(skills);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Onboarding {session.System}: {frameworkName}, {today:yyyy-MM-dd}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"System: {session.System}, product {session.Product}, owner {session.Owner}. Reviewed by {session.Reviewer}.");
        if (session.Groups.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" Implementation groups: {string.Join(", ", session.Groups)}.");
        }

        text.Append("\n\n");
        text.Append(session.Model is { } model
            ? $"Model: {model.Provider} {model.Model}. Every request and reply is in [requests/]({session.System}/requests/).\n"
            : "Model: none (--no-model); every proposal was left to the reviewer.\n");

        if (session.Supplier is { } supplier)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## Supplier\n\nSupplier: {supplier.EntityName} ({supplier.EntityRef}), solution {supplier.SolutionName}, contract {supplier.ContractRef}.\n");
            if (session.SupplierLevels.Count > 0)
            {
                text.Append("\n| Level | Value | Reason | Sources |\n| --- | --- | --- | --- |\n");
                foreach (var level in session.SupplierLevels)
                {
                    text.Append(CultureInfo.InvariantCulture, $"| {level.Name} | {level.Value} | {Cell(level.Reason)} | {Cell(string.Join(", ", level.Sources))} |\n");
                }
            }

            if (session.SupplierSummary.Count > 0)
            {
                text.Append("\nThis summary is a draft from the documents below; it is not evidence.\n\n");
                foreach (var point in session.SupplierSummary)
                {
                    text.Append(CultureInfo.InvariantCulture, $"- {point.Topic}: {point.Text} ({point.Source})\n");
                }
            }

            if (session.Documents.Count > 0)
            {
                text.Append("\nDocuments kept:\n\n| Source | Saved as | SHA-256 | Retrieved |\n| --- | --- | --- | --- |\n");
                foreach (var document in session.Documents)
                {
                    text.Append(CultureInfo.InvariantCulture, $"| {document.Source} | {document.Path} | `{document.Sha256}` | {document.RetrievedAt:yyyy-MM-dd HH:mm} UTC |\n");
                }
            }
        }

        if (skills.Count > 0)
        {
            text.Append("\n## Skills\n\n| Skill | Source | Version | SHA-256 |\n| --- | --- | --- | --- |\n");
            foreach (var skill in skills)
            {
                text.Append(CultureInfo.InvariantCulture, $"| {skill.Name} | {skill.Source} | {skill.Version} | `{skill.Sha256}` |\n");
            }
        }

        var decisions = session.Decisions;
        text.Append(CultureInfo.InvariantCulture, $"\n## Decisions\n\n{decisions.Count} requirements: {Count(decisions, DecisionKind.ApiCheck)} API check(s), {Count(decisions, DecisionKind.Manual)} manual, {Count(decisions, DecisionKind.NotApplicable)} not applicable, {Count(decisions, DecisionKind.OtherSystem)} for another system.\n\n");
        text.Append("| Requirement | Proposed | Decided | Reasoning | Accepted by | Justification, approver or system |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var decision in decisions)
        {
            var detail = decision.Kind switch
            {
                DecisionKind.NotApplicable => $"{decision.Justification} Approved by {decision.Approver}.",
                DecisionKind.OtherSystem => $"Provided by {decision.OtherSystem}.",
                _ => "",
            };
            text.Append(CultureInfo.InvariantCulture,
                $"| {decision.Key} | {(decision.Proposed is { } proposed ? Applicability.Name(proposed) : "(none)")} | {Applicability.Name(decision.Kind)} | {Cell(decision.Reasoning)} | {Cell(decision.AcceptedBy)} | {Cell(detail)} |\n");
        }

        return text.ToString();
    }

    private static int Count(IReadOnlyList<Decision> decisions, DecisionKind kind) => decisions.Count(d => d.Kind == kind);

    /// <summary>How a row names its requirement: <c>ref</c> when the key is the ref_id, else <c>urn</c>.</summary>
    private static string Key(Decision decision) => decision.Key == decision.RefId ? $"ref: {decision.Key}" : $"urn: {decision.Key}";

    private static string Cell(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Replace("|", "\\|", StringComparison.Ordinal);

    private static string Slug(string text) =>
        string.Join('-', new string([.. text.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A JSON string is a valid YAML double-quoted scalar.</summary>
    private static string Quote(string value) => JsonSerializer.Serialize(value);

    [GeneratedRegex("""^\s*-\s*(?:ref|urn):\s*"?(?<key>[^"\s#]+)"?""", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Named();
}
