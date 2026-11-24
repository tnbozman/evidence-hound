using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
