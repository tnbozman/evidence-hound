using System.Globalization;
using System.Text;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml;

/// <summary>
/// Writes what CISO Assistant holds as scope files: <c>setup.yaml</c>, <c>controls.yaml</c>, one file per audit under <c>audits/</c>, and a
/// snapshot of each framework under <c>frameworks/</c>. Pure. Everything is sorted, so exporting an unchanged instance again gives the same text.
/// </summary>
/// <remarks>
/// Left out, because collectors and manual sources rebuild them: rows a run computed (their observation ends with a run marker), rows still to
/// do and not assessed, and evidence in the root folder (where runs put theirs). Documents and controls are listed in each audit whose rows
/// link them, with those rows' requirements. A document lists the files it was given (downloaded with <c>--with-files</c>); otherwise none, and CISO
/// Assistant keeps them.
/// </remarks>
public static class ScopeExport
{
    private static readonly string Header = "# Written by hound scope export from what CISO Assistant holds. Review it, then manage CISO Assistant from these files.\n";

    public static IReadOnlyDictionary<string, string> Write(ScopeExportState state) => Write(state, new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>As above, listing each document's <paramref name="documentFiles"/> (by document id, relative to the compliance repo) as its files.</summary>
    public static IReadOnlyDictionary<string, string> Write(ScopeExportState state, IReadOnlyDictionary<string, IReadOnlyList<string>> documentFiles)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(documentFiles);

        var objects = state.Objects
            .GroupBy(o => o.Held.Kind)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Held).DistinctBy(o => o.Key).OrderBy(o => o.Key, StringComparer.Ordinal).ToArray());
        ScopeObject[] Of(ScopeKind kind) => objects.GetValueOrDefault(kind) ?? [];

        var documents = Of(ScopeKind.Document).Where(d => Field(d, "domain") is not null).ToArray();
        var documentIds = documents.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var snapshots = state.Frameworks.DistinctBy(f => f.FrameworkUrn).ToDictionary(f => f.FrameworkUrn, StringComparer.Ordinal);
        var used = Of(ScopeKind.Audit).Select(a => Field(a, "framework")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var frameworks = snapshots.Values.Where(s => used.Contains(s.FrameworkUrn)).OrderBy(s => s.FrameworkUrn, StringComparer.Ordinal).ToArray();

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["setup.yaml"] = Setup(state.RootFolder, frameworks, Of(ScopeKind.Domain), Of(ScopeKind.Perimeter), Of(ScopeKind.System)),
            ["controls.yaml"] = Controls(Of(ScopeKind.Control), documentIds),
        };
        foreach (var framework in frameworks)
        {
            files[$"frameworks/{Slug(framework.FrameworkUrn)}.json"] = FrameworkSnapshotJson.Write(framework);
        }

        var rows = Of(ScopeKind.Assessment)
            .Select(r => (Row: r, Parts: r.Key.Split('/', 2)))
            .Where(r => r.Parts.Length == 2)
            .ToLookup(r => r.Parts[0], r => (Requirement: r.Parts[1], r.Row), StringComparer.Ordinal);
        foreach (var audit in Of(ScopeKind.Audit))
        {
            var snapshot = Field(audit, "framework") is { } urn ? snapshots.GetValueOrDefault(urn) : null;
            var inScope = InGroups(snapshot, Split(Field(audit, "groups")));
            files[$"audits/{audit.Key.ToLowerInvariant()}.yaml"] = Audit(audit, [.. rows[audit.Key].Where(r => inScope(r.Requirement))], documents, documentIds, documentFiles);
        }

        return files;
    }

    private static string Setup(string rootFolder, FrameworkSnapshot[] frameworks, ScopeObject[] domains, ScopeObject[] perimeters, ScopeObject[] systems)
    {
        var yaml = new StringBuilder(Header)
            .Append("organisation:\n")
            .Append(CultureInfo.InvariantCulture, $"  root_folder: {Scalar(rootFolder)}\n")
            .Append(frameworks.Length == 0 ? "frameworks: []\n" : "frameworks:\n");
        foreach (var framework in frameworks)
        {
            yaml.Append(CultureInfo.InvariantCulture, $"  - urn: {Scalar(framework.FrameworkUrn)}\n")
                .Append(CultureInfo.InvariantCulture, $"    library: {Scalar(framework.LibraryUrn)}\n")
                .Append(CultureInfo.InvariantCulture, $"    snapshot: frameworks/{Slug(framework.FrameworkUrn)}.json\n");
        }

        yaml.Append(domains.Length == 0 ? "domains: []\n" : "domains:\n");
        foreach (var domain in domains)
        {
            yaml.Append(CultureInfo.InvariantCulture, $"  - name: {Scalar(domain.Key)}\n");
            Optional(yaml, "    ", "description", Field(domain, "description"));
            var mine = perimeters.Where(p => Field(p, "domain") == domain.Key).ToArray();
            if (mine.Length > 0)
            {
                yaml.Append("    perimeters:\n");
                foreach (var perimeter in mine.OrderBy(p => Field(p, "name"), StringComparer.Ordinal))
                {
                    yaml.Append(CultureInfo.InvariantCulture, $"      - name: {Scalar(Field(perimeter, "name") ?? perimeter.Key)}\n");
                    Optional(yaml, "        ", "ref_id", Field(perimeter, "ref_id"));
                    Optional(yaml, "        ", "description", Field(perimeter, "description"));
                }
            }

            var ownSystems = systems.Where(s => Field(s, "domain") == domain.Key).ToArray();
            if (ownSystems.Length > 0)
            {
                yaml.Append("    systems:\n");
                foreach (var system in ownSystems)
                {
                    yaml.Append(CultureInfo.InvariantCulture, $"      - name: {Scalar(system.Key)}\n");
                    Optional(yaml, "        ", "ref_id", Field(system, "ref_id"));
                    yaml.Append(CultureInfo.InvariantCulture, $"        type: {Field(system, "type") ?? "primary"}\n");
                    var (description, ai) = AiRegister.Read(Field(system, "description"), Split(Field(system, "labels")), new SourceLine("setup.yaml", 0));
                    Optional(yaml, "        ", "description", description);
                    Optional(yaml, "        ", "owner", Field(system, "owner"));
                    List(yaml, "        ", "parents", Split(Field(system, "parents")));
                    Ai(yaml, ai);
                }
            }
        }

        return yaml.ToString();
    }

    /// <summary>A system's AI register entry (s11-12), as <see cref="AiRegister.Read"/> recovered it from CISO Assistant.</summary>
    private static void Ai(StringBuilder yaml, AiRegistration? ai)
    {
        if (ai is null)
        {
            return;
        }

        yaml.Append("        ai:\n")
            .Append(CultureInfo.InvariantCulture, $"          tier: {Snake(ai.Tier)}\n")
            .Append(CultureInfo.InvariantCulture, $"          screened_on: {Scalar(Day(ai.ScreenedOn))}\n")
            .Append(CultureInfo.InvariantCulture, $"          use: {Snake(ai.Use)}\n")
            .Append(CultureInfo.InvariantCulture, $"          oversight: {Snake(ai.Oversight)}\n")
            .Append(CultureInfo.InvariantCulture, $"          oversight_note: {Scalar(ai.OversightNote)}\n");
        Optional(yaml, "          ", "impact_assessment", ai.ImpactAssessment is { } impact ? Snake(impact) : null);
        Optional(yaml, "          ", "impact_assessed_on", ai.ImpactAssessedOn is { } assessed ? Day(assessed) : null);
        Optional(yaml, "          ", "in_use_from", ai.InUseFrom is { } from ? Day(from) : null);
        Optional(yaml, "          ", "last_tested_on", ai.LastTestedOn is { } tested ? Day(tested) : null);
        Optional(yaml, "          ", "retired_on", ai.RetiredOn is { } retired ? Day(retired) : null);
    }

    private static string Snake<T>(T value)
        where T : struct, Enum =>
        AiRegister.Code(value).Replace('-', '_');

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Controls(ScopeObject[] controls, HashSet<string> documentIds)
    {
        var yaml = new StringBuilder(Header).Append(controls.Length == 0 ? "controls: []\n" : "controls:\n");
        foreach (var control in controls)
        {
            yaml.Append(CultureInfo.InvariantCulture, $"  - ref_id: {Scalar(control.Key)}\n")
                .Append(CultureInfo.InvariantCulture, $"    name: {Scalar(Field(control, "name") ?? control.Key)}\n");
            foreach (var field in new[] { "description", "domain", "owner", "priority", "category", "eta" })
            {
                Optional(yaml, "    ", field, Field(control, field));
            }

            List(yaml, "    ", "systems", Split(Field(control, "systems")));
            List(yaml, "    ", "documents", [.. Split(Field(control, "documents")).Where(documentIds.Contains)]);
        }

        return yaml.ToString();
    }

    private static string Audit(
        ScopeObject audit, (string Requirement, ScopeObject Row)[] rows, ScopeObject[] documents, HashSet<string> documentIds, IReadOnlyDictionary<string, IReadOnlyList<string>> documentFiles)
    {
        var yaml = new StringBuilder(Header)
            .Append(CultureInfo.InvariantCulture, $"ref_id: {Scalar(audit.Key)}\n")
            .Append(CultureInfo.InvariantCulture, $"name: {Scalar(Field(audit, "name") ?? audit.Key)}\n");
        Optional(yaml, "", "framework", Field(audit, "framework"));
        List(yaml, "", "groups", Split(Field(audit, "groups")), always: true);
        foreach (var field in new[] { "domain", "perimeter", "eta", "due_date" })
        {
            Optional(yaml, "", field, Field(audit, field));
        }

        List(yaml, "", "authors", Split(Field(audit, "authors")));
        List(yaml, "", "reviewers", Split(Field(audit, "reviewers")));

        var linked = (string field) => rows
            .SelectMany(r => Split(Field(r.Row, field)).Select(id => (Id: id, r.Requirement)))
            .GroupBy(l => l.Id, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, Requirements: g.Select(l => l.Requirement).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var cited = linked("documents").Where(d => documentIds.Contains(d.Key)).ToArray();
        if (cited.Length > 0)
        {
            yaml.Append("documents:\n");
            foreach (var (id, requirements) in cited)
            {
                yaml.Append(CultureInfo.InvariantCulture, $"  - id: {Scalar(id)}\n")
                    .Append(CultureInfo.InvariantCulture, $"    name: {Scalar(Field(documents.First(d => d.Key == id), "name") ?? id)}\n");
                List(yaml, "    ", "files", [.. documentFiles.GetValueOrDefault(id) ?? []]);
                List(yaml, "    ", "requirements", requirements);
            }
        }

        var controls = linked("controls");
        if (controls.Length > 0)
        {
            yaml.Append("controls:\n");
            foreach (var (refId, requirements) in controls)
            {
                yaml.Append(CultureInfo.InvariantCulture, $"  - ref_id: {Scalar(refId)}\n");
                List(yaml, "    ", "requirements", requirements);
            }
        }

        var hand = rows.Where(r => IsHandWritten(r.Row)).OrderBy(r => r.Requirement, StringComparer.Ordinal).ToArray();
        if (hand.Length > 0)
        {
            yaml.Append("assessments:\n");
            foreach (var (requirement, row) in hand)
            {
                var key = requirement.StartsWith("urn:", StringComparison.Ordinal) ? "urn" : "ref";
                yaml.Append(CultureInfo.InvariantCulture, $"  - {key}: {Scalar(requirement)}\n")
                    .Append(CultureInfo.InvariantCulture, $"    status: {Field(row, "status") ?? "to_do"}\n")
                    .Append(CultureInfo.InvariantCulture, $"    result: {Field(row, "result") ?? "not_assessed"}\n");
                Optional(yaml, "    ", "observation", Field(row, "observation"));
            }
        }

        return yaml.Append("unlisted:\n  result: not_assessed\n").ToString();
    }

    /// <summary>A row a person wrote: not computed by a run (no run marker), and not still to do and not assessed.</summary>
    private static bool IsHandWritten(ScopeObject row) =>
        RowState.ReadMark(Field(row, "observation")) is null
        && !(Field(row, "result") is null or "not_assessed" && Field(row, "status") is null or "to_do" && Field(row, "observation") is null);

    private static Func<string, bool> InGroups(FrameworkSnapshot? snapshot, string[] groups)
    {
        if (snapshot is null)
        {
            return _ => true;
        }

        var nodes = snapshot.Nodes.Where(n => groups.Length == 0 || n.Groups.Count == 0 || n.Groups.Any(groups.Contains)).ToArray();
        var keys = nodes.Select(n => n.Urn).Concat(nodes.Select(n => n.RefId).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        return keys.Contains;
    }

    private static void Optional(StringBuilder yaml, string indent, string key, string? value)
    {
        if (value is not null)
        {
            yaml.Append(CultureInfo.InvariantCulture, $"{indent}{key}: {Scalar(value)}\n");
        }
    }

    private static void List(StringBuilder yaml, string indent, string key, string[] values, bool always = false)
    {
        if (values.Length > 0 || always)
        {
            yaml.Append(CultureInfo.InvariantCulture, $"{indent}{key}: [{string.Join(", ", values.Order(StringComparer.Ordinal).Select(Scalar))}]\n");
        }
    }

    private static string? Field(ScopeObject o, string name) => o.Fields.GetValueOrDefault(name);

    private static string[] Split(string? joined) =>
        joined is null ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A file name for a framework: the last part of its URN.</summary>
    private static string Slug(string urn) => urn[(urn.LastIndexOf(':') + 1)..];

    /// <summary>
    /// A YAML scalar: plain when it is letters, digits, spaces and <c>. _ - @ / :</c> (no colon before a space) and cannot be read as anything but text; otherwise
    /// double-quoted with escapes, so text, dates and numbers all read back as the same string.
    /// </summary>
    private static string Scalar(string value)
    {
        var plain = value.Length > 0
            && char.IsAsciiLetter(value[0])
            && value[^1] is not (' ' or ':')
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '_' or '-' or '@' or '/' or ':')
            && !value.Contains("  ", StringComparison.Ordinal)
            && !value.Contains(": ", StringComparison.Ordinal)
            && value.ToLowerInvariant() is not ("true" or "false" or "null" or "yes" or "no" or "on" or "off" or "y" or "n");
        if (plain)
        {
            return value;
        }

        var quoted = new StringBuilder("\"");
        foreach (var c in value)
        {
            quoted.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
                _ => c.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }
}
