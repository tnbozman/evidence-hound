using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>The writes of what is assessed: frameworks, audits, documents, applied controls and rows.</summary>
internal sealed partial class ScopeWriter
{
    /// <summary>
    /// Loads a framework's library, or another library the scope names, such as a mapping set: a custom library file is uploaded first unless
    /// CISO Assistant already stores that library, then the library is imported by URN. A loaded library is never upgraded or unloaded here.
    /// </summary>
    private async Task<Result<Unit>> FrameworkAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        if (write.Change.Action != ScopeAction.Create || write.Declared is not { } declared)
        {
            return Result.Ok(Unit.Value);
        }

        if (declared.Initial.GetValueOrDefault("library") is not { } library)
        {
            return Result.Fail<Unit>(new Problem("scope.library_missing", $"Framework {declared.Key} names no library to load."));
        }

        // CISO Assistant loads a library when it is uploaded, so only a library that was already stored is imported.
        var uploaded = declared.Files is [var file, ..] ? await UploadLibraryAsync(library, file, cancellationToken) : Result.Ok(false);
        return await uploaded.BindAsync(done => done
            ? Task.FromResult(Result.Ok(Unit.Value))
            : SendAsync(HttpMethod.Post, $"stored-libraries/{library}/import/", null, $"to load library {library}", cancellationToken));
    }

    /// <summary>Uploads a custom library file unless CISO Assistant already stores the library; true when it was uploaded.</summary>
    private async Task<Result<bool>> UploadLibraryAsync(string library, ScopeFile file, CancellationToken cancellationToken)
    {
        var stored = await http.ListAllAsync($"stored-libraries/?urn={Uri.EscapeDataString(library)}", cancellationToken);
        return await stored.BindAsync(async found =>
        {
            if (found.Any(l => Text(l["urn"]) == library))
            {
                return Result.Ok(false);
            }

            // CISO Assistant reads the upload with a file upload parser: the raw file as the body, named by Content-Disposition.
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(file.Path, cancellationToken));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = Path.GetFileName(file.Path) };
            var request = new HttpRequestMessage(HttpMethod.Post, "stored-libraries/upload/") { Content = content };
            return (await http.SendForJsonAsync(request, $"to upload library {Path.GetFileName(file.Path)}", cancellationToken)).Map(_ => true);
        });
    }

    /// <summary>An audit (a compliance assessment) by <c>ref_id</c>, in its domain and perimeter, with its framework, groups, dates and people.</summary>
    private async Task<Result<Unit>> AuditAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("compliance-assessments/", cancellationToken);
        return await listed.BindAsync(async audits =>
        {
            var existing = IdByRefId(audits, ScopeKind.Audit).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("compliance-assessments", existing, write, cancellationToken);
            }

            var folder = folders.FolderOf(declared);
            var framework = await FrameworkIdAsync(Field(declared, "framework"), cancellationToken);
            var perimeter = await folder.BindAsync(f => PerimeterIdAsync(Field(declared, "perimeter"), f, cancellationToken));
            var authors = await ActorsAsync(Split(Field(declared, "authors")), cancellationToken);
            var reviewers = await ActorsAsync(Split(Field(declared, "reviewers")), cancellationToken);
            return await Result.Combine([folder.Map(Node), framework.Map(Node), perimeter.Map(Node), authors.Map(a => (JsonNode)a), reviewers.Map(r => (JsonNode)r)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["framework"] = resolved[1].DeepClone(),
                    ["perimeter"] = resolved[2].DeepClone(),
                    ["selected_implementation_groups"] = new JsonArray([.. Split(Field(declared, "groups")).Select(g => JsonValue.Create(g))]),
                    ["eta"] = Field(declared, "eta"),
                    ["due_date"] = Field(declared, "due_date"),
                    ["authors"] = resolved[3].DeepClone(),
                    ["reviewers"] = resolved[4].DeepClone(),
                };
                // A period audit: its status and lock go in the same write, because a locked audit refuses any later one.
                body["status"] = Field(declared, "status");
                if (Field(declared, "locked") == "true")
                {
                    body["is_locked"] = true;
                }

                // A new period opens from the previous one, so its results and evidence links carry over.
                if (write.Change.Action == ScopeAction.Create && declared.Initial.GetValueOrDefault("baseline") is { } baseline)
                {
                    body["baseline"] = IdByField(audits, "ref_id").GetValueOrDefault(baseline);
                }

                Drop(body, write.Change, "eta", "due_date");
                Drop(body, "status", "baseline");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "compliance-assessments/", body, $"to create audit {declared.Key}", cancellationToken)
                    : await UpdateAsync("compliance-assessments", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>A document (an evidence item) by its id as <c>ref_id</c>, in its domain, then each of its files CISO Assistant does not hold yet, by SHA-256.</summary>
    private async Task<Result<Unit>> DocumentAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("evidences/", cancellationToken);
        return await listed.BindAsync(async evidences =>
        {
            var existing = IdByRefId(evidences, ScopeKind.Document).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("evidences", existing, write, cancellationToken);
            }

            return await folders.FolderOf(declared).BindAsync(async folder =>
            {
                var body = new JsonObject { ["name"] = Field(declared, "name"), ["ref_id"] = declared.Key, ["description"] = Described(declared, write.Change), ["folder"] = folder };
                var written = write.Change.Action == ScopeAction.Create
                    ? (await SendForJsonAsync(HttpMethod.Post, "evidences/", body, $"to create document {declared.Key}", cancellationToken)).Map(Id)
                    : existing is null
                        ? Result.Fail<string>(ApiProblems.NotFound("document", $"'{declared.Key}'"))
                        : (await SendAsync(HttpMethod.Patch, $"evidences/{Uri.EscapeDataString(existing)}/", body, $"to update document {declared.Key}", cancellationToken)).Map(_ => existing);
                return await written.BindAsync(id => UploadFilesAsync(id, declared, cancellationToken));
            });
        });
    }

    private async Task<Result<Unit>> UploadFilesAsync(string evidenceId, ScopeObject declared, CancellationToken cancellationToken)
    {
        if (declared.Files.Count == 0)
        {
            return Result.Ok(Unit.Value);
        }

        var revisions = await http.ListAllAsync($"evidence-revisions/?evidence={Uri.EscapeDataString(evidenceId)}", cancellationToken);
        return await revisions.BindAsync(async held =>
        {
            var hashes = held.Select(r => Text(r["attachment_hash"])).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in declared.Files.Where(f => !hashes.Contains(f.Sha256)).DistinctBy(f => f.Sha256))
            {
                var content = await File.ReadAllBytesAsync(file.Path, cancellationToken);
                var sent = await client.UploadEvidenceRevisionAsync(evidenceId, new EvidenceFile(Path.GetFileName(file.Path), content), cancellationToken);
                if (sent.Match(_ => false, _ => true))
                {
                    return sent;
                }
            }

            return Result.Ok(Unit.Value);
        });
    }

    /// <summary>An applied control by <c>ref_id</c>, in its domain, with its owners, systems and documents; its status and reference control on create only.</summary>
    private async Task<Result<Unit>> ControlAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("applied-controls/", cancellationToken);
        return await listed.BindAsync(async controls =>
        {
            var existing = IdByRefId(controls, ScopeKind.Control).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("applied-controls", existing, write, cancellationToken);
            }

            var create = write.Change.Action == ScopeAction.Create;
            var folder = folders.FolderOf(declared);
            var owners = await ActorsAsync(Split(Field(declared, "owner")), cancellationToken);
            var systems = await IdsAsync("assets/", "name", Split(Field(declared, "systems")), "system", cancellationToken);
            var documents = await IdsAsync("evidences/", "ref_id", Split(Field(declared, "documents")), "document", cancellationToken);
            var reference = create ? await ReferenceControlAsync(declared.Initial.GetValueOrDefault("reference_control"), cancellationToken) : Result.Ok<string[]>([]);
            return await Result.Combine([folder.Map(Node), owners.Map(o => (JsonNode)o), systems.Map(s => (JsonNode)s), documents.Map(d => (JsonNode)d)]).BindAsync(resolved => reference.BindAsync(async referenceId =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["owner"] = resolved[1].DeepClone(),
                    ["priority"] = int.TryParse(Field(declared, "priority"), NumberStyles.None, CultureInfo.InvariantCulture, out var priority) ? priority : null,
                    ["category"] = Field(declared, "category"),
                    ["eta"] = Field(declared, "eta"),
                    ["assets"] = resolved[2].DeepClone(),
                    ["evidences"] = resolved[3].DeepClone(),
                };
                if (create)
                {
                    body["status"] = declared.Initial.GetValueOrDefault("status");
                    body["reference_control"] = referenceId is [var id] ? id : null;
                }

                Drop(body, write.Change, "priority", "category", "eta", "status", "reference_control");
                return create
                    ? await SendAsync(HttpMethod.Post, "applied-controls/", body, $"to create applied control {declared.Key}", cancellationToken)
                    : await UpdateAsync("applied-controls", existing, body, write, cancellationToken);
            }));
        });
    }

    /// <summary>
    /// A row: the fields the scope declares (a result written by hand or by the <c>unlisted</c> rule, linked controls and documents), written to
    /// each row of the requirement within the audit's selected groups.
    /// </summary>
    private async Task<Result<Unit>> RowAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        if (write.Declared is not { } declared || declared.Key.Split('/', 2) is not [var audit, var requirement])
        {
            return Result.Ok(Unit.Value);
        }

        // A ref may name rows in several groups; the scope writes those in the audit's selected groups (validation checks the ref is not ambiguous there).
        var key = requirement.StartsWith("urn:", StringComparison.Ordinal) ? RequirementKey.Urn(requirement) : RequirementKey.Ref(requirement, allRows: true);
        var found = await client.FindAuditAsync(audit, cancellationToken);
        var rows = await found.BindAsync(async a => (await client.FindRequirementAssessmentsAsync(audit, key, cancellationToken)).Bind(all => RequirementRows.InScope(key, all, a.SelectedGroups)));
        var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
        var documents = await IdsAsync("evidences/", "ref_id", Split(Field(declared, "documents")), "document", cancellationToken);
        return await rows.BindAsync(inScope => controls.BindAsync(controlIds => documents.BindAsync(async documentIds =>
        {
            var body = new JsonObject
            {
                ["status"] = Field(declared, "status"),
                ["result"] = Field(declared, "result"),
                ["observation"] = Field(declared, "observation"),
                ["applied_controls"] = declared.Fields.ContainsKey("controls") ? controlIds : null,
                ["evidences"] = declared.Fields.ContainsKey("documents") ? documentIds : null,
            };
            Drop(body, "status", "result", "observation", "applied_controls", "evidences");
            foreach (var row in inScope)
            {
                var sent = await SendAsync(HttpMethod.Patch, $"requirement-assessments/{Uri.EscapeDataString(row.Id)}/", (JsonObject)body.DeepClone(), $"to update row {declared.Key}", cancellationToken);
                if (sent.Match(_ => false, _ => true))
                {
                    return sent;
                }
            }

            return Result.Ok(Unit.Value);
        })));
    }

    private async Task<Result<string>> FrameworkIdAsync(string? urn, CancellationToken cancellationToken) =>
        (await http.ListAllAsync("frameworks/", cancellationToken)).Bind(frameworks =>
            urn is not null && IdByField(frameworks, "urn").GetValueOrDefault(urn) is { } id ? Result.Ok(id) : Result.Fail<string>(ApiProblems.NotFound("framework", $"'{urn}'")));

    private async Task<Result<string>> PerimeterIdAsync(string? name, string folder, CancellationToken cancellationToken) =>
        (await http.ListAllAsync("perimeters/", cancellationToken)).Bind(perimeters =>
            perimeters.FirstOrDefault(p => Text(p["name"]) == name && RelationId(p["folder"]) == folder) is { } found
                ? Result.Ok(Id(found))
                : Result.Fail<string>(ApiProblems.NotFound("perimeter", $"'{name}'")));

    /// <summary>The reference control's ID, when the control names one by URN.</summary>
    private async Task<Result<string[]>> ReferenceControlAsync(string? urn, CancellationToken cancellationToken)
    {
        if (urn is null)
        {
            return Result.Ok<string[]>([]);
        }

        return (await http.ListAllAsync($"reference-controls/?urn={Uri.EscapeDataString(urn)}", cancellationToken)).Bind(found =>
            IdByField(found, "urn").GetValueOrDefault(urn) is { } id ? Result.Ok<string[]>([id]) : Result.Fail<string[]>(ApiProblems.NotFound("reference control", $"'{urn}'")));
    }

    /// <summary>The IDs of the objects of a list named by one field, in the order named; every name must be found.</summary>
    private async Task<Result<JsonArray>> IdsAsync(string path, string field, string[] names, string what, CancellationToken cancellationToken)
    {
        if (names.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        return (await http.ListAllAsync(path, cancellationToken)).Bind(all => Resolve(names, IdByField(all, field), what));
    }

    /// <summary>Each object's ID by its <c>ref_id</c>, or, when it has none, by the one an export made from its name; the write then sets it.</summary>
    private static Dictionary<string, string> IdByRefId(List<JsonNode> objects, ScopeKind kind) =>
        objects
            .Select(o => (Key: Text(o["ref_id"]) ?? (Text(o["name"]) is { } name ? ScopeRefIds.For(kind, name) : null), Id: Id(o)))
            .Where(o => o.Key is not null)
            .GroupBy(o => o.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

    private static Dictionary<string, string> IdByField(List<JsonNode> objects, string field) =>
        objects.Where(o => Text(o[field]) is not null).GroupBy(o => Text(o[field])!).ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);

    private static JsonNode Node(string id) => JsonValue.Create(id);
}
