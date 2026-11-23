using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Onboarding.Explain;

/// <summary>What a change touches, which decides the skills that guide its review.</summary>
public enum ChangeTopic
{
    ThirdParty,
    NotApplicable,
    Profile,
    Mapping,
    Audit,
}

public sealed record PlanCount(int Value, string Label);

/// <summary>
/// A plan or diff read in code: its counts, the objects it names, what needs a careful look, and the text to show the model. The counts and
/// flags come from code alone; they are the source of truth a model's summary is checked against.
/// </summary>
public sealed record ChangeFacts(
    string Source,
    string Kind,
    IReadOnlyList<PlanCount> Counts,
    IReadOnlyList<string> Objects,
    IReadOnlyList<string> Flags,
    IReadOnlyList<ChangeTopic> Topics,
    string Text)
{
    public string CountsText => string.Join(", ", Counts.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Value} {c.Label}"))) + ".";
}

/// <summary>Reads a scope plan (Markdown or JSON), a collector plan (JSON) or a unified diff into <see cref="ChangeFacts"/>. Pure.</summary>
public static partial class ChangeReader
{
    private static readonly HashSet<string> ThirdPartyKinds = ["entity", "main_entity", "mainentity", "solution", "contract", "representative"];

    public static Result<ChangeFacts> Read(string text, string source) => Read(text, source, new Dictionary<string, string>());

    /// <summary>As <see cref="Read(string, string)"/>, also flagging checks a diff removes from a profile that <paramref name="mappings"/> (path to text) still use.</summary>
    public static Result<ChangeFacts> Read(string text, string source, IReadOnlyDictionary<string, string> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(text);
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var trimmed = normalised.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            return Json(normalised, source);
        }

        if (normalised.Contains("## Scope plan", StringComparison.Ordinal))
        {
            return MarkdownPlan(normalised, source);
        }

        return normalised.Split('\n').Any(l => l.StartsWith("+++ ", StringComparison.Ordinal))
            ? Result.Ok(DiffFacts(normalised, source, mappings))
            : Unreadable(source);
    }

    /// <summary>A minimal unified diff of two versions of one file: the lines only in <paramref name="before"/>, then those only in <paramref name="after"/>.</summary>
    public static string Diff(string before, string after, string name)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var old = Lines(before);
        var updated = Lines(after);
        var removed = Without(old, updated);
        var added = Without(updated, old);
        var diff = new StringBuilder().Append(CultureInfo.InvariantCulture, $"--- a/{name}\n+++ b/{name}\n");
        foreach (var line in removed)
        {
            diff.Append('-').Append(line).Append('\n');
        }

        foreach (var line in added)
        {
            diff.Append('+').Append(line).Append('\n');
        }

        return diff.ToString();
    }

    private static Result<ChangeFacts> Unreadable(string source) =>
        Result.Fail<ChangeFacts>(new Problem("explain.unreadable", $"{source} is not a scope plan, a collector plan or a diff. Give the output of 'hound scope plan', 'hound plan' or 'git diff', or two versions of a file."));

    private static Result<ChangeFacts> MarkdownPlan(string text, string source)
    {
        var summary = PlanSummary().Match(text);
        if (!summary.Success)
        {
            return Unreadable(source);
        }

        var rows = PlanRow().Matches(text).Select(m => new Row(m.Groups["action"].Value.ToLowerInvariant(), m.Groups["kind"].Value, m.Groups["key"].Value.Trim(), m.Groups["fields"].Value)).ToArray();
        var drifted = summary.Groups["drift"].Success ? Number(summary.Groups["drift"].Value) : 0;
        var shown = AccessReview().Replace(text, "");
        return Result.Ok(PlanFacts(source, rows, Number(summary.Groups["create"].Value), Number(summary.Groups["update"].Value), Number(summary.Groups["delete"].Value), drifted, shown));
    }

    private static Result<ChangeFacts> Json(string text, string source)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return Unreadable(source);
        }

        if (root?["changes"] is JsonArray changes)
        {
            var rows = changes.OfType<JsonObject>().Select(c => new Row(
                ((string?)c["action"] ?? "").ToLowerInvariant(),
                (string?)c["kind"] ?? "",
                (string?)c["key"] ?? "",
                JsonFields(c["fields"]))).ToArray();
            var drifted = root["drift"] is JsonArray drift ? drift.Count : 0;
            var shown = string.Join('\n', rows.Select(r => $"- {r.Action} {r.Kind} {r.Key}: {r.Fields}"));
            return Result.Ok(PlanFacts(source, rows, rows.Count(r => r.Action == "create"), rows.Count(r => r.Action == "update"), rows.Count(r => r.Action == "delete"), drifted, shown));
        }

        if (root?["audits"] is JsonArray audits)
        {
            var listed = audits.OfType<JsonObject>().Select(a => (Audit: (string?)a["compliance_assessment"] ?? "", Changed: Amount(a["changed"]), Unchanged: Amount(a["unchanged"]))).ToArray();
            var shown = string.Join('\n', listed.Select(a => string.Create(CultureInfo.InvariantCulture, $"- audit {a.Audit}: {a.Changed} changed, {a.Unchanged} unchanged")));
            return Result.Ok(new ChangeFacts(
                source,
                "plan",
                [new(listed.Length, listed.Length == 1 ? "audit" : "audits"), new(listed.Sum(a => a.Changed), "changed"), new(listed.Sum(a => a.Unchanged), "unchanged")],
                [.. listed.Select(a => a.Audit)],
                [],
                [ChangeTopic.Audit],
                shown));
        }

        return Unreadable(source);
    }

    private static ChangeFacts PlanFacts(string source, IReadOnlyList<Row> rows, int create, int update, int delete, int drifted, string shown)
    {
        var flags = new List<string>();
        var topics = new HashSet<ChangeTopic>();
        foreach (var row in rows)
        {
            if (ThirdPartyKinds.Contains(row.Kind))
            {
                topics.Add(ChangeTopic.ThirdParty);
            }

            if (row.Kind is "audit" or "assessment")
            {
                topics.Add(ChangeTopic.Audit);
            }

            if (row.Action == "delete")
            {
                flags.Add($"Deletes {row.Kind} {row.Key}.");
            }

            if (row.Kind == "system" && row.Action != "delete" && OwnerMissing(row.Fields))
            {
                flags.Add($"System {row.Key} has no owner.");
            }

            if (row.Fields.Contains("not_applicable", StringComparison.Ordinal))
            {
                topics.Add(ChangeTopic.NotApplicable);
                flags.Add($"Marks {row.Key} not applicable.");
            }
        }

        if (drifted > 0)
        {
            flags.Add(string.Create(CultureInfo.InvariantCulture, $"{drifted} object(s) drifted: changed in CISO Assistant or outside the scope."));
        }

        return new ChangeFacts(
            source,
            "plan",
            [new(create, "to create"), new(update, "to update"), new(delete, "to delete"), new(drifted, "drifted")],
            [.. rows.Select(r => r.Key).Distinct(StringComparer.Ordinal)],
            flags,
            [.. topics],
            shown);
    }

    private static ChangeFacts DiffFacts(string text, string source, IReadOnlyDictionary<string, string> mappings)
    {
        var files = new List<FileChange>();
        string? from = null;
        FileChange? current = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                from = PathOf(line[4..]);
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var to = PathOf(line[4..]);
                current = new FileChange(to ?? from ?? "(unknown)", to is null);
                files.Add(current);
                continue;
            }

            if (current is null || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("@@", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('+'))
            {
                current.Added.Add(line[1..]);
            }
            else if (line.StartsWith('-'))
            {
                current.Removed.Add(line[1..]);
            }
        }

        var flags = new List<string>();
        var topics = new HashSet<ChangeTopic>();
        var objects = new List<string>();
        foreach (var file in files)
        {
            objects.Add(file.Path);
            objects.AddRange(file.Added.Concat(file.Removed).Select(l => RefLine().Match(l)).Where(m => m.Success).Select(m => m.Groups["ref"].Value));
            foreach (var topic in TopicsOf(file.Path))
            {
                topics.Add(topic);
            }

            if (file.Deleted)
            {
                flags.Add($"Deletes {file.Path}.");
            }

            var notApplicable = file.Added.Count(l => NotApplicableLine().IsMatch(l));
            if (notApplicable > 0)
            {
                topics.Add(ChangeTopic.NotApplicable);
                flags.Add(string.Create(CultureInfo.InvariantCulture, $"Marks {notApplicable} requirement(s) not applicable in {file.Path}."));
            }

            var removedChecks = file.Removed.Count(l => CheckLine().IsMatch(l));
            if (removedChecks > 0 && (Has(file.Path, "mappings") || Has(file.Path, "profiles")))
            {
                flags.Add(string.Create(CultureInfo.InvariantCulture, $"Removes {removedChecks} line(s) naming checks or requirements from {file.Path}."));
            }

            if (Has(file.Path, "profiles"))
            {
                var kept = file.Added.Select(l => CheckId().Match(l)).Where(m => m.Success).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
                foreach (var id in file.Removed.Select(l => CheckId().Match(l)).Where(m => m.Success).Select(m => m.Groups["id"].Value).Where(id => !kept.Contains(id)).Distinct(StringComparer.Ordinal))
                {
                    flags.AddRange(mappings.Where(m => Names(m.Value, id)).OrderBy(m => m.Key, StringComparer.Ordinal)
                        .Select(m => $"Removes check {id} from {file.Path}, but {m.Key} still uses it."));
                }
            }

            if (Has(file.Path, "third-parties"))
            {
                flags.AddRange(file.Added.Select(l => ContractStatus().Match(l)).Where(m => m.Success && m.Groups["status"].Value != "active")
                    .Select(m => $"A contract in {file.Path} is {m.Groups["status"].Value}, not active: the supplier is not approved yet."));
            }
        }

        var added = files.Sum(f => f.Added.Count);
        var removed = files.Sum(f => f.Removed.Count);
        return new ChangeFacts(
            source,
            "diff",
            [new(files.Count, files.Count == 1 ? "file changed" : "files changed"), new(added, added == 1 ? "line added" : "lines added"), new(removed, removed == 1 ? "line removed" : "lines removed")],
            [.. objects.Distinct(StringComparer.Ordinal)],
            flags,
            [.. topics],
            text);
    }

    private static IEnumerable<ChangeTopic> TopicsOf(string path)
    {
        if (Has(path, "third-parties"))
        {
            yield return ChangeTopic.ThirdParty;
        }

        if (Has(path, "profiles"))
        {
            yield return ChangeTopic.Profile;
        }

        if (Has(path, "mappings"))
        {
            yield return ChangeTopic.Mapping;
        }

        if (Has(path, "audits"))
        {
            yield return ChangeTopic.Audit;
        }
    }

    /// <summary>Whether a plan row's fields (<c>field: new</c> or <c>field: old → new</c>, joined by <c>&lt;br&gt;</c>) leave the owner empty.</summary>
    private static bool OwnerMissing(string fields) =>
        fields.Split("<br>").Select(f => f.Trim()).Any(f => f == "owner:" || (f.StartsWith("owner:", StringComparison.Ordinal) && (f.EndsWith("→ (none)", StringComparison.Ordinal) || f.EndsWith('→'))));

    private static string JsonFields(JsonNode? fields) =>
        fields is not JsonArray array ? fields?.ToJsonString() ?? ""
        : string.Join("<br>", array.OfType<JsonObject>().Select(f => (Field: (string?)f["field"] ?? "", Old: (string?)f["old"], New: (string?)f["new"]))
            .Select(f => f.Old is null ? $"{f.Field}: {f.New}" : $"{f.Field}: {f.Old} → {f.New ?? "(none)"}"));

    private static bool Names(string text, string id) =>
        Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(id)}(?![\w.])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool Has(string path, string folder) => path.Split('/').Contains(folder, StringComparer.Ordinal);

    private static string? PathOf(string header)
    {
        var path = header.Split('\t')[0].Trim();
        return path == "/dev/null" ? null : path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
    }

    private static List<string> Lines(string text) => [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Where(l => l.Length > 0)];

    /// <summary>The lines of <paramref name="lines"/> left after taking out one of each line in <paramref name="other"/>, in order.</summary>
    private static List<string> Without(List<string> lines, List<string> other)
    {
        var remaining = other.GroupBy(l => l, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var left = new List<string>();
        foreach (var line in lines)
        {
            if (remaining.TryGetValue(line, out var count) && count > 0)
            {
                remaining[line] = count - 1;
            }
            else
            {
                left.Add(line);
            }
        }

        return left;
    }

    private static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    private static int Amount(JsonNode? node) => node switch
    {
        JsonArray array => array.Count,
        JsonValue value when value.TryGetValue<int>(out var number) => number,
        _ => 0,
    };

    private sealed record Row(string Action, string Kind, string Key, string Fields);

    private sealed record FileChange(string Path, bool Deleted)
    {
        public List<string> Added { get; } = [];

        public List<string> Removed { get; } = [];
    }

    [GeneratedRegex(@"(?<create>\d+) to create, (?<update>\d+) to update, (?<delete>\d+) to delete\. (?:No drift|(?<drift>\d+) drifted)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlanSummary();

    [GeneratedRegex(@"^\| (?<action>Create|Update|Delete) \| (?<kind>[a-z_]+) \| (?<key>.*?) \| (?<fields>.*)\|$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlanRow();

    [GeneratedRegex(@"^### Access review\n.*?(?=^### |\z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AccessReview();

    [GeneratedRegex(@"^\s*(?:-\s*)?(?:ref_id|ref)\s*:\s*""?(?<ref>[^""\s#]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RefLine();

    [GeneratedRegex(@"^\s*result\s*:\s*""?not_applicable", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotApplicableLine();

    [GeneratedRegex(@"^\s*(?:-\s*(?:ref|urn|id|check)\s*:|checks\s*:)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CheckLine();

    [GeneratedRegex(@"^\s*-\s*id\s*:\s*""?(?<id>[A-Za-z0-9_.-]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CheckId();

    [GeneratedRegex(@"^\s*status\s*:\s*""?(?<status>[a-z_]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContractStatus();
}
