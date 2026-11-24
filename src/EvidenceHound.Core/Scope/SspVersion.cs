using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What a plan was built from: the model, with every value and its source, and the hand-written sections' text (s11-05).</summary>
public sealed record SspSnapshot(SspDocument Document, IReadOnlyDictionary<string, string> Sections);

/// <summary>
/// Draft hashes and signing (s11-05). A version is signed when <c>scope/ssp.yaml</c> records the authorising official's sign-off with the
/// SHA-256 of the draft they reviewed; the signed plan is that draft, rebuilt from its snapshot, with the approval in place of the draft
/// marks. Pure.
/// </summary>
public static class SspVersion
{
    /// <summary>The SHA-256 of the text as UTF-8, in lower-case hex.</summary>
    public static string Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The signed plan, or a problem when the sign-off is for another version or the snapshot no longer gives the reviewed draft.</summary>
    public static Result<string> Sign(SspSnapshot snapshot, SspSignOff signOff)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(signOff);
        if (snapshot.Document.Version.Value != signOff.Version)
        {
            return Result.Fail<string>(new Problem(
                "ssp.signoff_version_mismatch", $"the sign-off is for version {signOff.Version}, but the snapshot is version {snapshot.Document.Version.Value}."));
        }

        var draft = Hash(SspLayout.Markdown(snapshot.Document, snapshot.Sections));
        return draft == signOff.Sha256
            ? Result.Ok(SspLayout.Markdown(snapshot.Document, snapshot.Sections, signOff))
            : Result.Fail<string>(new Problem(
                "ssp.signoff_hash_mismatch",
                $"version {signOff.Version} was signed with draft SHA-256 {signOff.Sha256}, but this snapshot gives {draft}: it is not the draft that was reviewed."));
    }
}

/// <summary>The snapshot file: the plan's model and section texts as JSON, so a signed version can be rebuilt without CISO Assistant.</summary>
public static class SspSnapshotJson
{
    public static readonly string Format = "hound-ssp-snapshot/1";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(SspSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sections = new JsonObject();
        foreach (var (name, text) in snapshot.Sections.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            sections[name] = text;
        }

        return new JsonObject { ["format"] = Format, ["document"] = SspJson.Object(snapshot.Document), ["section_texts"] = sections }.ToJsonString(Indented) + "\n";
    }

    public static Result<SspSnapshot> Read(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("the file is not a JSON object");
            if ((string?)root["format"] != Format)
            {
                throw new JsonException($"'format' is not {Format}");
            }

            var sections = (root["section_texts"] as JsonObject ?? []).ToDictionary(p => p.Key, p => (string?)p.Value ?? "", StringComparer.Ordinal);
            return Result.Ok(new SspSnapshot(Document(Object(root, "document")), sections));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return Result.Fail<SspSnapshot>(new Problem("ssp.snapshot_invalid", $"{source}: not a valid plan snapshot: {ex.Message}"));
        }
    }

    private static SspDocument Document(JsonObject d) =>
        new(
            Text(d, "programme"),
            Text(d, "profile"),
            Value(d, "system_name"),
            Value(d, "version"),
            Value(d, "framework"),
            Value(d, "audit"),
            Value(d, "perimeter"),
            [.. Items(d, "roles").Select(r => new SspRoleValue(Text(r, "role"), Value(r, "holder")))],
            [.. Items(d, "inventory").Select(s => new SspSystem(Value(s, "name"), Value(s, "type"), Value(s, "owner"), Value(s, "description")))],
            [.. Items(d, "external_services").Select(s => new SspService(Value(s, "system"), Value(s, "solution"), Value(s, "supplier"), Value(s, "criticality")))],
            [.. Items(d, "controls").Select(Control)],
            [.. Items(d, "requirements").Select(Requirement)],
            [.. (d["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(Value)],
            [.. Items(d, "sections").Select(s => new SspNamed(Text(s, "name"), Value(s, "file")))])
        {
            Classification = Value(d, "classification"),
            OperationalStatus = Value(d, "operational_status"),
            SystemType = Value(d, "system_type"),
            Annex = [.. Items(d, "annex").Select(Annex)],
        };

    private static SspControl Control(JsonObject c) =>
        new(Value(c, "ref_id"), Value(c, "name"), Value(c, "owner"), Value(c, "status"), Value(c, "description"))
        {
            Eta = Value(c, "eta"),
            Systems = Strings(c, "systems"),
            ResponsibleEntity = c["responsible_entity"] is JsonObject entity ? Value(entity) : null,
        };

    private static SspRequirement Requirement(JsonObject r) =>
        new(Value(r, "ref"), Value(r, "result"), Value(r, "observation"), Value(r, "evidence"), Value(r, "controls"))
        {
            Status = Value(r, "status"),
            Statement = Value(r, "statement"),
            Responsible = Value(r, "responsible"),
            Gaps = Strings(r, "gaps"),
        };

    private static SspAnnexRow Annex(JsonObject a) =>
        new(
            Text(a, "control"),
            Text(a, "title"),
            Strings(a, "listed_under"),
            Value(a, "applicability"),
            Value(a, "status"),
            Value(a, "statement"),
            Value(a, "responsible"),
            Value(a, "evidence"),
            (string?)a["poam"])
        {
            Description = (string?)a["description"] ?? "",
            MaturityLevels = Strings(a, "maturity_levels"),
            ControlTypes = Strings(a, "control_types"),
            ResponsibleEntity = Value(a, "responsible_entity"),
            AsdStatus = Value(a, "asd_status"),
        };

    private static SspValue Value(JsonObject parent, string key) => Value(Object(parent, key));

    private static SspValue Value(JsonObject value)
    {
        var source = Object(value, "source");
        return new SspValue((string?)value["value"], new SspSource(Text(source, "kind"), Text(source, "reference")));
    }

    private static JsonObject Object(JsonObject parent, string key) =>
        parent[key] as JsonObject ?? throw new JsonException($"'{key}' is missing");

    private static string Text(JsonObject parent, string key) =>
        (string?)parent[key] ?? throw new JsonException($"'{key}' is missing");

    private static IEnumerable<JsonObject> Items(JsonObject parent, string key) => (parent[key] as JsonArray ?? []).OfType<JsonObject>();

    private static string[] Strings(JsonObject parent, string key) =>
        [.. (parent[key] as JsonArray ?? []).Select(v => v?.GetValue<string>() ?? "")];
}

/// <summary>One change between two versions of a plan: its area (boundary, inventory, supplier, status, statement, section), the item, and the values.</summary>
public sealed record SspChange(string Area, string Item, string? Before, string? After);

/// <summary>What changed between two versions (s11-05), boundary and inventory first. Pure.</summary>
public static class SspDiff
{
    private static readonly string[] BoundaryAreas = ["boundary", "inventory"];

    public static IReadOnlyList<SspChange> Of(SspSnapshot before, SspSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var (a, b) = (before.Document, after.Document);
        return
        [
            .. Changed("boundary", "system name", a.SystemName.Value, b.SystemName.Value),
            .. Changed("boundary", "perimeter", a.Perimeter.Value, b.Perimeter.Value),
            .. Changed("boundary", "classification", a.Classification.Value, b.Classification.Value),
            .. Compare("inventory", a.Inventory, b.Inventory, s => s.Name.Value, s => $"{s.Type.Value}; owner {s.Owner.Value}"),
            .. Compare("supplier", a.ExternalServices, b.ExternalServices, s => s.System.Value, s => $"{s.Solution.Value} from {s.Supplier.Value}"),
            .. Compare("status", a.Requirements, b.Requirements, r => r.Ref.Value, r => r.Status.Value),
            .. Compare("statement", a.Requirements, b.Requirements, r => r.Ref.Value, r => r.Statement.Value),
            .. Compare("section", before.Sections.ToArray(), after.Sections.ToArray(), s => s.Key, s => s.Value),
        ];
    }

    public static string Markdown(string from, string to, IReadOnlyList<SspChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"## Changes from version {from} to {to}\n\n");
        if (changes.Count == 0)
        {
            return text.Append("No changes.\n").ToString();
        }

        if (changes.Any(c => BoundaryAreas.Contains(c.Area, StringComparer.Ordinal)))
        {
            text.Append("The boundary or inventory changed: this usually needs a new major version.\n\n");
        }

        text.Append("| Area | Item | Before | After |\n| --- | --- | --- | --- |\n");
        foreach (var c in changes)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {c.Area} | {c.Item} | {Cell(c.Before)} | {Cell(c.After)} |\n");
        }

        return text.ToString();
    }

    private static IEnumerable<SspChange> Changed(string area, string item, string? before, string? after) =>
        before == after ? [] : [new SspChange(area, item, before, after)];

    /// <summary>Items added, removed or changed, by key in ordinal order.</summary>
    private static IEnumerable<SspChange> Compare<T>(string area, IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, string?> key, Func<T, string?> value)
    {
        var a = before.Where(x => key(x) is not null).GroupBy(x => key(x)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => value(g.First()), StringComparer.Ordinal);
        var b = after.Where(x => key(x) is not null).GroupBy(x => key(x)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => value(g.First()), StringComparer.Ordinal);
        return a.Keys.Union(b.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(k => !a.TryGetValue(k, out var old) || !b.TryGetValue(k, out var current) || old != current)
            .Select(k => new SspChange(area, k, a.GetValueOrDefault(k), b.GetValueOrDefault(k)));
    }

    private static string Cell(string? value) =>
        value is null ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}

/// <summary>The evidence revision <c>hound ssp publish</c> uploads (s11-05): one zip with fixed timestamps, so the same inputs give the same bytes. Pure.</summary>
public static class SspPackage
{
    private static readonly DateTimeOffset Stamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static EvidenceFile Zip(string programme, string version, string plan, string poam, string snapshot, EvidenceFile? report)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(poam);
        ArgumentNullException.ThrowIfNull(snapshot);
        var name = $"{programme}-{version}";
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, $"ssp-{name}.md", Encoding.UTF8.GetBytes(plan));
            Add(zip, $"poam-{name}.md", Encoding.UTF8.GetBytes(poam));
            Add(zip, $"ssp-{name}.snapshot.json", Encoding.UTF8.GetBytes(snapshot));
            if (report is not null)
            {
                Add(zip, report.FileName, report.Content.ToArray());
            }
        }

        return new EvidenceFile($"ssp-{name}.zip", buffer.ToArray());
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = Stamp;
        using var stream = entry.Open();
        stream.Write(content);
    }
}
