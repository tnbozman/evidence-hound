using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Reads what CISO Assistant holds under the scope's root folder, with GET requests only. Lists find the objects in the root folder and the
/// domains directly under it; each object's <c>/object/</c> view gives its fields as IDs and codes, which are turned into the names the scope
/// uses. Fields come out exactly as <see cref="ScopeDesired"/> writes them, so an unchanged object compares equal.
/// </summary>
internal sealed class ScopeStateReader(HttpClient http) : IScopeStateSource, IScopeExportSource
{
    public async Task<Result<ScopeExportState>> ReadAllAsync(string rootFolder, CancellationToken cancellationToken)
    {
        var reading = new Reading(http, cancellationToken);
        var held = await ReadHeldAsync(new ScopeStateQuery(rootFolder, [], new Dictionary<string, IReadOnlyList<RequirementKey>>()), allRows: true, reading);
        return await held.BindAsync(async h =>
        {
            var snapshots = new List<FrameworkSnapshot>();
            foreach (var urn in h.AuditedFrameworks)
            {
                if (h.Frameworks.FirstOrDefault(f => Text(f["urn"]) == urn) is { } framework && await SnapshotAsync(reading, framework, h.Libraries) is { } snapshot)
                {
                    snapshots.Add(snapshot);
                }
            }

            return reading.Problems.Count > 0
                ? Result.Fail<ScopeExportState>(reading.Problems)
                : Result.Ok(new ScopeExportState(rootFolder, h.Objects, snapshots));
        });
    }

    public async Task<Result<IReadOnlyList<ExportedFile>>> DownloadAsync(string documentId, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("evidences/", cancellationToken);
        return await listed.BindAsync(async evidences =>
        {
            if (evidences.FirstOrDefault(e => RefIdOrName(ScopeKind.Document, e) == documentId) is not { } evidence)
            {
                return Result.Fail<IReadOnlyList<ExportedFile>>(ApiProblems.NotFound("document", $"'{documentId}'"));
            }

            using var response = await http.GetAsync(new Uri($"evidences/{Uri.EscapeDataString(Id(evidence))}/attachment/", UriKind.Relative), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Result.Ok<IReadOnlyList<ExportedFile>>([]);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<IReadOnlyList<ExportedFile>>(ApiProblems.FromStatus((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), $"to download the file of document '{documentId}'"));
            }

            var disposition = response.Content.Headers.ContentDisposition;
            var name = Path.GetFileName((disposition?.FileNameStar ?? disposition?.FileName ?? documentId).Trim('"'));
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return Result.Ok<IReadOnlyList<ExportedFile>>(content.Length == 0 ? [] : [new ExportedFile(name.Length > 0 ? name : documentId, content)]);
        });
    }

    public async Task<Result<IReadOnlyList<UserAccess>>> ReadAccessAsync(CancellationToken cancellationToken)
    {
        var reading = new Reading(http, cancellationToken);
        var folders = await reading.ListAsync("folders/");
        var users = await reading.ListAsync("users/");
        var roles = await reading.ListAsync("roles/");
        var assignments = await reading.ListAsync("role-assignments/");
        if (reading.Problems.Count > 0)
        {
            return Result.Fail<IReadOnlyList<UserAccess>>(reading.Problems);
        }

        var folderNames = Names(folders, "name");
        var roleNames = RoleCodes.ById(roles, Id);
        string Held(JsonNode assignment) =>
            $"{Lookup(assignment["role"], roleNames)} on {string.Join(", ", (assignment["perimeter_folders"] as JsonArray ?? []).Select(f => Lookup(f, folderNames)).OfType<string>())}";
        return Result.Ok<IReadOnlyList<UserAccess>>(
        [
            .. users.Select(u =>
            {
                var groups = (u["user_groups"] as JsonArray ?? []).Select(RelationId).OfType<string>().ToHashSet(StringComparer.Ordinal);
                var held = assignments.Where(a => RelationId(a["user"]) == Id(u) || (RelationId(a["user_group"]) is { } group && groups.Contains(group)));
                return new UserAccess(
                    (Text(u["email"]) ?? Id(u)).ToLowerInvariant(),
                    u["has_mfa_enabled"] is JsonValue mfa && mfa.TryGetValue<bool>(out var enabled) && enabled,
                    [.. held.Select(Held).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            }),
        ]);
    }

    public async Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return (await ReadHeldAsync(query, allRows: false, new Reading(http, cancellationToken))).Map(h => h.Objects);
    }

    /// <summary>
    /// What CISO Assistant holds under the root folder: the query's frameworks and rows, or, with <paramref name="allRows"/>, the frameworks
    /// of every audit and every row of every audit.
    /// </summary>
    private static async Task<Result<HeldState>> ReadHeldAsync(ScopeStateQuery query, bool allRows, Reading reading)
    {
        var folders = await reading.ListAsync("folders/");
        if (reading.Problems.Count > 0)
        {
            return Result.Fail<HeldState>(reading.Problems);
        }

        if (folders.FirstOrDefault(f => Text(f["name"]) == query.RootFolder) is not { } root)
        {
            return Result.Fail<HeldState>(ApiProblems.NotFound("root folder", $"name '{query.RootFolder}'"));
        }

        var rootId = Id(root);
        var domainIds = folders.Where(f => RelationId(f["parent_folder"]) == rootId).Select(Id).ToArray();
        var domainObjects = await reading.ObjectsAsync("folders", domainIds);
        var userList = await reading.ListAsync("users/");
        var users = Actors(userList, await reading.ListAsync("actors/"));
        var frameworkList = await reading.ListAsync("frameworks/");
        var frameworks = frameworkList.ToDictionary(Id, f => Text(f["urn"]) ?? Id(f), StringComparer.Ordinal);
        var libraries = await reading.ListAsync("loaded-libraries/");
        HashSet<string> inScope = [rootId, .. domainIds];
        var perimeters = await reading.ObjectsInAsync("perimeters", inScope);
        var assets = await reading.ObjectsInAsync("assets", inScope);
        var audits = await reading.ObjectsInAsync("compliance-assessments", inScope);
        var evidences = await reading.ObjectsInAsync("evidences", inScope);
        var planned = evidences.Where(IsPlanned).ToArray();
        var tasks = query.EvidencePlan ? (await reading.ListAsync("task-templates/")).Where(t => RelationId(t["folder"]) is { } f && inScope.Contains(f)).ToArray() : [];
        var controls = await reading.ObjectsInAsync("applied-controls", inScope);
        var hashes = await HashesAsync(reading, evidences);
        var access = query.Access ? await AccessAsync(reading, folders, userList, inScope) : [];
        var settings = query.Settings ? await SettingsAsync(reading) : [];
        var entityList = query.ThirdParties || query.Organisation ? await reading.ListAsync("entities/") : [];
        var thirdParties = query.ThirdParties
            ? new ThirdPartyLists(entityList, await reading.ListAsync("solutions/"), await reading.ListAsync("contracts/"), await reading.ListAsync("representatives/"))
            {
                Assessments = await reading.ListAsync("entity-assessments/"),
                Tiers = await reading.ListAsync("tiers/"),
            }
            : null;
        var labelled = planned.Length > 0 || assets.Concat(thirdParties?.Solutions ?? []).Any(o => o["filtering_labels"] is JsonArray { Count: > 0 });
        var labels = labelled ? Names(await reading.ListAsync("filtering-labels/"), "label") : [];
        var auditedFrameworks = audits.Select(a => Lookup(a["framework"], frameworks)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (allRows)
        {
            query = query with { FrameworkUrns = auditedFrameworks };
        }

        var rows = allRows
            ? await reading.AllRowsAsync(audits.Select(a => (Id(a), AuditKey(a))))
            : await reading.RowsAsync(query, audits.Select(a => Text(a["ref_id"])).OfType<string>().ToHashSet(StringComparer.Ordinal));
        if (!allRows && query.AllRowsOf.Count > 0)
        {
            // Every other row of the audits the scope declares, read only so a plan can show its effect on compliance.
            var known = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            var everyRow = await reading.AllRowsAsync(audits.Where(a => query.AllRowsOf.Contains(AuditKey(a))).Select(a => (Id(a), AuditKey(a))));
            rows.AddRange(everyRow.Where(r => known.Add(r.Key)));
        }

        if (reading.Problems.Count > 0)
        {
            return Result.Fail<HeldState>(reading.Problems);
        }

        var rowKeys = rows.GroupBy(r => Id(r.Row), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        var names = new Lookups(
            domainObjects.ToDictionary(Id, d => Text(d["name"]) ?? Id(d), StringComparer.Ordinal),
            Text(root["name"]) ?? query.RootFolder,
            users,
            assets.ToDictionary(Id, a => Text(a["name"]) ?? Id(a), StringComparer.Ordinal),
            perimeters.ToDictionary(Id, p => Text(p["name"]) ?? Id(p), StringComparer.Ordinal),
            evidences.ToDictionary(Id, e => Text(e["ref_id"]) ?? Id(e), StringComparer.Ordinal),
            controls.ToDictionary(Id, c => Text(c["ref_id"]) ?? Id(c), StringComparer.Ordinal))
        {
            Labels = labels,
        };
        IReadOnlyList<ActualObject> objects =
        [
            .. Frameworks(query, frameworkList, libraries),
            .. domainObjects.Select(d => Marked(ScopeKind.Domain, Text(d["name"]), d)),
            .. perimeters.Select(p => Marked(ScopeKind.Perimeter, ScopeDesired.PerimeterKey(names.Domain(p) ?? names.Root, Text(p["name"]) ?? Id(p)), p,
                ("name", Text(p["name"])), ("ref_id", Text(p["ref_id"])), ("domain", names.Domain(p)))),
            .. assets.Select(a => Marked(ScopeKind.System, Text(a["name"]), a,
                ("ref_id", Text(a["ref_id"])), ("type", AssetType(Text(a["type"]))), ("owner", Joined(a["owner"], names.Users)), ("domain", names.Domain(a)),
                ("parents", Joined(a["parent_assets"], names.Assets)), ("labels", Joined(a["filtering_labels"], labels)))),
            .. audits.Select(a => Marked(ScopeKind.Audit, AuditKey(a), a,
                ("name", Text(a["name"])), ("framework", Lookup(a["framework"], frameworks)), ("groups", ScopeDesired.Joined(Strings(a["selected_implementation_groups"]))),
                ("domain", names.Domain(a)), ("perimeter", Lookup(a["perimeter"], names.Perimeters)), ("eta", Text(a["eta"])), ("due_date", Text(a["due_date"])),
                ("authors", Joined(a["authors"], names.Users)), ("reviewers", Joined(a["reviewers"], names.Users)), ("status", Text(a["status"])),
                ("locked", IsLocked(a) ? "true" : null)) with { Locked = IsLocked(a) }),
            .. planned.Select(e => Marked(ScopeKind.Evidence, Text(e["ref_id"]), e,
                ("name", Text(e["name"])), ("domain", names.Domain(e)), ("owner", Joined(e["owner"], names.Users)), ("controls", Joined(e["applied_controls"], names.Controls)),
                ("labels", Joined(e["filtering_labels"], labels)))),
            .. tasks.Select(t => Marked(ScopeKind.TaskTemplate, Text(t["ref_id"]), t,
                ("name", Text(t["name"])), ("domain", names.Domain(t)), ("assignees", Joined(t["assigned_to"], names.Users)), ("schedule", Schedule(t["schedule"])),
                ("evidence", Joined(t["evidences"], names.Evidence)), ("controls", Joined(t["applied_controls"], names.Controls)),
                ("requirements", Joined(t["requirement_assessments"], rowKeys)), ("enabled", t["enabled"] is JsonValue e && e.TryGetValue<bool>(out var enabled) && !enabled ? "false" : "true"))),
            .. evidences.Where(e => !IsPlanned(e)).Select(e => Marked(ScopeKind.Document, RefIdOrName(ScopeKind.Document, e), e, ("name", Text(e["name"])), ("domain", names.Domain(e)), ("hashes", hashes.GetValueOrDefault(Id(e))))),
            .. controls.Select(c => Marked(ScopeKind.Control, RefIdOrName(ScopeKind.Control, c), c,
                ("name", Text(c["name"])), ("domain", names.Domain(c)), ("owner", Joined(c["owner"], names.Users)), ("priority", Text(c["priority"])),
                ("category", Text(c["category"])), ("eta", Text(c["eta"])), ("systems", Joined(c["assets"], names.Assets)), ("documents", Joined(c["evidences"], names.Evidence)))),
            .. rows.Select(r => new ActualObject(
                new ScopeObject(ScopeKind.Assessment, r.Key, Fields(
                    ("status", Choice(Text(r.Row["status"]))), ("result", Choice(Text(r.Row["result"]))), ("observation", Text(r.Row["observation"])),
                    ("controls", Joined(r.Row["applied_controls"], names.Controls)), ("documents", Joined(r.Row["evidences"], names.Evidence)))),
                null)),
            .. access,
            .. settings,
            .. query.Organisation ? MainEntity(entityList) : [],
            .. thirdParties is null ? [] : ThirdParties(thirdParties, names),
        ];
        return Result.Ok(new HeldState(objects, frameworkList, libraries.ToDictionary(Id, l => (Text(l["urn"]), Text(l["version"])), StringComparer.Ordinal), auditedFrameworks));
    }

    /// <summary>
    /// Users (marked in their observation), each with the roles their user groups give as <c>roles</c>, and the teams in the scope's folders.
    /// Role assignments made straight to a user cannot be made through the API, so they are not read as roles here; the access review
    /// (<see cref="ReadAccessAsync"/>) reports every role a user holds, however it was given.
    /// </summary>
    private static async Task<List<ActualObject>> AccessAsync(Reading reading, List<JsonNode> folders, List<JsonNode> users, HashSet<string> inScope)
    {
        var teams = await reading.ObjectsInAsync("teams", inScope);
        var groups = await reading.ListAsync("user-groups/");
        var folderNames = Names(folders, "name");
        var emails = users.ToDictionary(Id, u => (Text(u["email"]) ?? Id(u)).ToLowerInvariant(), StringComparer.Ordinal);
        var groupRoles = groups.Select(g => (Id: Id(g), Role: RoleCodes.GroupRole(g, folderNames))).Where(g => g.Role is not null).ToDictionary(g => g.Id, g => g.Role!, StringComparer.Ordinal);
        return
        [
            .. users.Select(u =>
            {
                var (_, state) = ScopeMarker.Read(Text(u["observation"]));
                var active = u["is_active"] is JsonValue value && value.TryGetValue<bool>(out var isActive) && !isActive ? "false" : "true";
                var roles = (u["user_groups"] as JsonArray ?? []).Select(RelationId).OfType<string>().Select(groupRoles.GetValueOrDefault).OfType<string>();
                return new ActualObject(
                    new ScopeObject(ScopeKind.User, emails[Id(u)], Fields(
                        ("first_name", Text(u["first_name"])), ("last_name", Text(u["last_name"])), ("active", active), ("roles", ScopeDesired.Roles(roles)))),
                    state);
            }),
            .. teams.Select(t => Marked(ScopeKind.Team, Text(t["name"]), t,
                ("domain", Lookup(t["folder"], folderNames)), ("leader", Lookup(t["leader"], emails)), ("members", Joined(t["members"], emails)),
                ("email", Text(t["team_email"])))),
        ];
    }

    /// <summary>Every feature flag and general setting, each as an object whose <c>value</c> is its JSON; the plan compares only those the scope names.</summary>
    private static async Task<List<ActualObject>> SettingsAsync(Reading reading)
    {
        var flags = await reading.ObjectAsync("settings/feature-flags/", "to read the feature flags") as JsonObject ?? [];
        var general = (await reading.ObjectAsync("settings/general/", "to read the general settings"))?["value"] as JsonObject ?? [];
        static ActualObject Setting(ScopeKind kind, string name, string value) =>
            new(new ScopeObject(kind, name, new Dictionary<string, string>(StringComparer.Ordinal) { ["value"] = value }), null);
        return
        [
            .. flags.Where(f => f.Value is JsonValue v && v.TryGetValue<bool>(out _)).Select(f => Setting(ScopeKind.FeatureFlag, f.Key, f.Value!.GetValue<bool>() ? "true" : "false")),
            .. general.Select(g => Setting(ScopeKind.Setting, g.Key, CisoAssistantSettings.Json(g.Value))),
        ];
    }

    /// <summary>An evidence item of the evidence plan: its ref_id follows the EV- convention. Any other evidence item is a document.</summary>
    private static bool IsPlanned(JsonNode evidence) => Text(evidence["ref_id"]) is { } refId && RefIds.Follows(RefIdKind.Evidence, refId);

    /// <summary>A task schedule as the evidence plan writes it: "every 1 week" or "every N months", a yearly schedule as 12.</summary>
    private static string? Schedule(JsonNode? schedule)
    {
        var interval = schedule?["interval"] is JsonValue i && i.TryGetValue<int>(out var n) ? n : 1;
        return Text(schedule?["frequency"]) is { } frequency ? TaskSchedules.Text(new TaskSchedule(frequency, interval)) : null;
    }

    private static Dictionary<string, string> Names(List<JsonNode> objects, string field) =>
        objects.ToDictionary(Id, o => Text(o[field]) ?? Id(o), StringComparer.Ordinal);

    /// <summary>What a read found, with what an export also needs: every framework, each loaded library's URN and version, and the audits' frameworks.</summary>
    private sealed record HeldState(IReadOnlyList<ActualObject> Objects, List<JsonNode> Frameworks, Dictionary<string, (string? Urn, string? Version)> Libraries, string[] AuditedFrameworks);

    /// <summary>An object's <c>ref_id</c>, or one made from its name when it has none, so the scope files can name it.</summary>
    private static string RefIdOrName(ScopeKind kind, JsonNode held) =>
        Text(held["ref_id"]) ?? (Text(held["name"]) is { } name ? ScopeRefIds.For(kind, name) : Id(held));

    private static string AuditKey(JsonNode audit) => RefIdOrName(ScopeKind.Audit, audit);

    /// <summary>A framework's snapshot from its own record and its assessable requirement nodes; null when either cannot be read.</summary>
    private static async Task<FrameworkSnapshot?> SnapshotAsync(Reading reading, JsonNode framework, Dictionary<string, (string? Urn, string? Version)> libraries)
    {
        var id = Id(framework);
        var held = (await reading.ObjectsAsync("frameworks", [id])).FirstOrDefault();
        var nodes = await reading.ListAsync($"requirement-nodes/?framework={Uri.EscapeDataString(id)}");
        if (held is null)
        {
            return null;
        }

        var libraryId = RelationId(held["library"]) ?? RelationId(framework["library"]);
        (string? Urn, string? Version)? library = libraryId is not null && libraries.TryGetValue(libraryId, out var found) ? found : null;
        var version = int.TryParse(library?.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
        SnapshotNode[] assessable =
        [
            .. nodes.Where(n => n["assessable"] is JsonValue a && a.TryGetValue<bool>(out var isAssessable) && isAssessable).Select(n => new SnapshotNode(
                Text(n["urn"]) ?? Id(n),
                Text(n["ref_id"]),
                Text(n["parent_urn"]),
                Strings(n["implementation_groups"]),
                Text(n["name"]) ?? Text(n["description"]) ?? Text(n["ref_id"]) ?? Text(n["urn"]) ?? "")),
        ];
        ImplementationGroup[] defined =
            [.. (held["implementation_groups_definition"] as JsonArray ?? []).OfType<JsonObject>().Select(g => new ImplementationGroup(Text(g["ref_id"]) ?? "", Text(g["name"]) ?? Text(g["ref_id"]) ?? ""))];
        return new FrameworkSnapshot(
            library?.Urn ?? (framework["library"] is JsonObject listed ? Text(listed["urn"]) : null) ?? "",
            Text(held["urn"]) ?? Text(framework["urn"]) ?? id,
            version,
            defined.Length > 0 ? defined : [.. assessable.SelectMany(n => n.Groups).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(g => new ImplementationGroup(g, g))],
            assessable);
    }

    /// <summary>
    /// The frameworks and other libraries the scope loads that CISO Assistant holds, each with its loaded library's version: a framework by its
    /// URN, another library (such as a mapping set) by its own URN.
    /// </summary>
    private static IEnumerable<ActualObject> Frameworks(ScopeStateQuery query, List<JsonNode> frameworks, List<JsonNode> libraries)
    {
        var versions = libraries.ToDictionary(Id, l => Text(l["version"]), StringComparer.Ordinal);
        foreach (var urn in query.FrameworkUrns.Distinct(StringComparer.Ordinal))
        {
            (bool Found, string? Version) version = frameworks.FirstOrDefault(f => Text(f["urn"]) == urn) is { } framework
                ? (true, RelationId(framework["library"]) is { } library ? versions.GetValueOrDefault(library) : null)
                : libraries.FirstOrDefault(l => Text(l["urn"]) == urn) is { } loaded ? (true, Text(loaded["version"])) : (false, null);
            if (version.Found)
            {
                yield return new ActualObject(new ScopeObject(ScopeKind.Framework, urn, Fields(("version", version.Version))), null);
            }
        }
    }

    /// <summary>Each evidence item's file hashes, from its revisions.</summary>
    private static async Task<Dictionary<string, string?>> HashesAsync(Reading reading, List<JsonNode> evidences)
    {
        var hashes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in evidences.Select(Id))
        {
            var revisions = await reading.ListAsync($"evidence-revisions/?evidence={Uri.EscapeDataString(id)}");
            hashes[id] = ScopeDesired.Joined(revisions.Select(r => Text(r["attachment_hash"])).OfType<string>().Distinct(StringComparer.Ordinal));
        }

        return hashes;
    }

    /// <summary>
    /// Names for actor IDs, which owners, authors and reviewers hold: a user's actor by the user's email, a team's or entity's by its name.
    /// </summary>
    private static Dictionary<string, string> Actors(List<JsonNode> users, List<JsonNode> actors)
    {
        var emails = users.ToDictionary(Id, u => Text(u["email"]) ?? Id(u), StringComparer.Ordinal);
        return actors.ToDictionary(Id, a => RelationId(a["specific"]) is { } id && emails.TryGetValue(id, out var email) ? email : Text(a["str"]) ?? Id(a), StringComparer.Ordinal);
    }

    private static bool Matches(JsonNode? requirement, RequirementKey key) =>
        Text(requirement?[key.Kind == RequirementKeyKind.Urn ? "urn" : "ref_id"]) == key.Value;

    /// <summary>
    /// Entities (but the organisation's own), solutions, contracts and representatives, keyed and linked by <c>ref_id</c> (representatives by
    /// email), as <see cref="ScopeDesired"/> declares them.
    /// </summary>
    private static IEnumerable<ActualObject> ThirdParties(ThirdPartyLists lists, Lookups names)
    {
        var entities = lists.Entities.Where(e => !IsBuiltin(e)).ToArray();
        var entityRefs = entities.ToDictionary(Id, e => Text(e["ref_id"]) ?? Id(e), StringComparer.Ordinal);
        var solutionRefs = lists.Solutions.ToDictionary(Id, s => Text(s["ref_id"]) ?? Id(s), StringComparer.Ordinal);
        var tiers = lists.Tiers.ToDictionary(Id, t => Text(t["key"]) ?? Text(t["name"]) ?? Id(t), StringComparer.Ordinal);
        return
        [
            .. entities.Select(e => Marked(ScopeKind.Entity, Text(e["ref_id"]), e,
                ("name", Text(e["name"])), ("mission", Text(e["mission"])), ("country", Text(e["country"])), ("currency", Text(e["currency"])),
                ("parent", Lookup(e["parent_entity"], entityRefs)), ("domain", names.Domain(e)), ("dependency", Text(e["default_dependency"])),
                ("penetration", Text(e["default_penetration"])), ("maturity", Text(e["default_maturity"])), ("trust", Text(e["default_trust"])),
                ("identifiers", Identifiers(e["legal_identifiers"]))) with
            {
                Observed = Fields(("tier", Lookup(e["tier"], tiers))),
            }),
            .. lists.Assessments.Where(a => RelationId(a["entity"]) is { } id && entityRefs.ContainsKey(id)).Select(a => Marked(ScopeKind.EntityAssessment, Text(a["ref_id"]), a,
                ("name", Text(a["name"])), ("entity", Lookup(a["entity"], entityRefs)), ("solutions", Joined(a["solutions"], solutionRefs)), ("domain", names.Domain(a)),
                ("perimeter", Lookup(a["perimeter"], names.Perimeters)), ("due_date", Text(a["due_date"])), ("criticality", Text(a["criticality"]) is "0" ? null : Text(a["criticality"])),
                ("conclusion", Choice(Text(a["conclusion"])))) with
            {
                Observed = Fields(("conclusion", Choice(Text(a["conclusion"]))), ("expiry_date", Text(a["expiry_date"]))),
            }),
            .. lists.Solutions.Where(s => RelationId(s["provider_entity"]) is { } id && entityRefs.ContainsKey(id)).Select(s => Marked(ScopeKind.Solution, Text(s["ref_id"]), s,
                ("name", Text(s["name"])), ("entity", Lookup(s["provider_entity"], entityRefs)), ("criticality", Text(s["criticality"])),
                ("labels", Joined(s["filtering_labels"], names.Labels)))),
            .. lists.Contracts.Select(c => Marked(ScopeKind.Contract, Text(c["ref_id"]), c,
                ("name", Text(c["name"])), ("entity", Lookup(c["provider_entity"], entityRefs)), ("domain", names.Domain(c)), ("status", Choice(Text(c["status"]))),
                ("start_date", Text(c["start_date"])), ("end_date", Text(c["end_date"])), ("annual_expense", ScopeDesired.Amount(Text(c["annual_expense"]))),
                ("currency", Text(c["currency"])), ("solutions", Joined(c["solutions"], solutionRefs)))),
            .. lists.Representatives.Select(r => Marked(ScopeKind.Representative, Text(r["email"])?.ToLowerInvariant(), r,
                ("entity", Lookup(r["entity"], entityRefs)), ("first_name", Text(r["first_name"])), ("last_name", Text(r["last_name"])), ("role", Text(r["role"])),
                ("phone", Text(r["phone"])))),
        ];
    }

    /// <summary>The organisation's own entity (the builtin main entity) as <see cref="ScopeDesired"/> declares it. It carries no marker.</summary>
    private static IEnumerable<ActualObject> MainEntity(List<JsonNode> entities) =>
        entities.Where(IsBuiltin).Take(1).Select(e => new ActualObject(new ScopeObject(ScopeKind.MainEntity, ScopeDesired.OrganisationKey, Fields(
            ("name", Text(e["name"])), ("description", Text(e["description"])), ("mission", Text(e["mission"])), ("country", Text(e["country"])),
            ("currency", Text(e["currency"])), ("identifiers", Identifiers(e["legal_identifiers"])))), null));

    private static bool IsBuiltin(JsonNode entity) => entity["builtin"] is JsonValue b && b.TryGetValue<bool>(out var builtin) && builtin;

    /// <summary>
    /// Legal identifiers as <see cref="ScopeDesired.Identifiers"/> writes them: a JSON object with sorted keys, or null when empty. v4.3.4's read
    /// serializer gives them as display text, one <c>name: value</c> per line; an object is read as is.
    /// </summary>
    private static string? Identifiers(JsonNode? identifiers) =>
        ScopeDesired.Identifiers(identifiers switch
        {
            JsonObject map => map.Where(i => Text(i.Value) is not null).ToDictionary(i => i.Key, i => Text(i.Value)!, StringComparer.Ordinal),
            _ => (Text(identifiers) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(": ", 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal),
        });

    private sealed record ThirdPartyLists(List<JsonNode> Entities, List<JsonNode> Solutions, List<JsonNode> Contracts, List<JsonNode> Representatives)
    {
        public List<JsonNode> Assessments { get; init; } = [];

        public List<JsonNode> Tiers { get; init; } = [];
    }

    /// <summary>An object with its description's marker: the text is a field, the marker is the state <c>hound</c> last wrote.</summary>
    private static ActualObject Marked(ScopeKind kind, string? key, JsonNode held, params (string Name, string? Value)[] fields)
    {
        var (description, state) = ScopeMarker.Read(Text(held["description"]));
        return new ActualObject(new ScopeObject(kind, key ?? Id(held), Fields([.. fields, ("description", description)])), state);
    }

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);

    private static string? AssetType(string? code) => code switch
    {
        "PR" => "primary",
        "SP" => "supporting",
        _ => Choice(code),
    };

    /// <summary>A choice's value from its value or its display name: "To do" is "to_do".</summary>
    private static string? Choice(string? value) => value?.Trim().ToLowerInvariant().Replace(' ', '_');

    private static string? Lookup(JsonNode? relation, Dictionary<string, string> names) =>
        RelationId(relation) is { } id ? names.GetValueOrDefault(id, id) : null;

    private static string? Joined(JsonNode? relations, Dictionary<string, string> names) =>
        ScopeDesired.Joined((relations as JsonArray ?? []).Select(RelationId).OfType<string>().Select(id => names.GetValueOrDefault(id, id)));

    /// <summary>A related object's ID: a plain ID, or the <c>{"id", "str"}</c> object the read serializers give.</summary>
    private static string? RelationId(JsonNode? relation) => relation is JsonObject o ? Text(o["id"]) : Text(relation);

    private static bool IsLocked(JsonNode audit) => audit["is_locked"] is JsonValue locked && locked.TryGetValue<bool>(out var isLocked) && isLocked;

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => string.IsNullOrEmpty(text) ? null : text,
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    private static List<string> Strings(JsonNode? node) => [.. (node as JsonArray ?? []).Select(Text).OfType<string>()];

    private static string Id(JsonNode node) => Text(node["id"]) ?? throw new InvalidOperationException("CISO Assistant returned an object without an id.");

    /// <summary>Names for the IDs the objects refer to. Objects in the root folder have no domain.</summary>
    private sealed record Lookups(
        Dictionary<string, string> Domains,
        string Root,
        Dictionary<string, string> Users,
        Dictionary<string, string> Assets,
        Dictionary<string, string> Perimeters,
        Dictionary<string, string> Evidence,
        Dictionary<string, string> Controls)
    {
        /// <summary>Filtering labels by ID, read only when something in scope carries one.</summary>
        public Dictionary<string, string> Labels { get; init; } = [];

        public string? Domain(JsonNode held) => RelationId(held["folder"]) is { } id ? Domains.GetValueOrDefault(id) : null;
    }

    /// <summary>One read of CISO Assistant: every request goes on, and the first failures are kept to report at the end.</summary>
    private sealed class Reading(HttpClient http, CancellationToken cancellationToken)
    {
        public List<Problem> Problems { get; } = [];

        /// <summary>Every object of a list. <paramref name="path"/> may carry a query of its own.</summary>
        public async Task<List<JsonNode>> ListAsync(string path) =>
            Problems.Count > 0 ? [] : Kept(await http.ListAllAsync(path, cancellationToken)) ?? [];

        /// <summary>The <c>/object/</c> view of each listed object whose folder is one of <paramref name="folders"/>.</summary>
        public async Task<List<JsonNode>> ObjectsInAsync(string resource, HashSet<string> folders) =>
            await ObjectsAsync(resource, [.. (await ListAsync($"{resource}/")).Where(o => RelationId(o["folder"]) is { } f && folders.Contains(f)).Select(Id)]);

        /// <summary>The row of each requirement the scope writes, from each audit CISO Assistant already holds, matched by requirement ref_id or URN.</summary>
        public async Task<List<(string Key, JsonNode Row)>> RowsAsync(ScopeStateQuery query, HashSet<string> heldAudits)
        {
            var found = new List<(string Key, JsonNode Row)>();
            foreach (var (audit, requirements) in query.Rows.Where(r => heldAudits.Contains(r.Key)))
            {
                var rows = await ListAsync($"requirement-assessments/?{Selector.RowsOfAudit(audit).ToQueryString()}");
                found.AddRange(requirements
                    .Select(requirement => (Key: ScopeDesired.AssessmentKey(audit, requirement), Row: rows.FirstOrDefault(r => Matches(r["requirement"], requirement))))
                    .Where(r => r.Row is not null)
                    .Select(r => (r.Key, r.Row!)));
            }

            return found;
        }

        /// <summary>Every row of each audit, keyed by its requirement's ref_id, or by its URN when the ref is missing or repeated in that audit.</summary>
        public async Task<List<(string Key, JsonNode Row)>> AllRowsAsync(IEnumerable<(string Id, string Key)> audits)
        {
            var found = new List<(string Key, JsonNode Row)>();
            foreach (var (id, key) in audits)
            {
                var rows = await ListAsync($"requirement-assessments/?compliance_assessment={Uri.EscapeDataString(id)}");
                var repeated = rows.Select(r => Text(r["requirement"]?["ref_id"])).OfType<string>().GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
                found.AddRange(rows.Select(r => (Ref: Text(r["requirement"]?["ref_id"]), Urn: Text(r["requirement"]?["urn"]), Row: r))
                    .Where(r => r.Ref is not null || r.Urn is not null)
                    .Select(r => ($"{key}/{(r.Ref is { } refId && !repeated.Contains(refId) ? refId : r.Urn ?? r.Ref)}", r.Row)));
            }

            return found;
        }

        public async Task<List<JsonNode>> ObjectsAsync(string resource, string[] ids)
        {
            var objects = new List<JsonNode>(ids.Length);
            foreach (var id in ids)
            {
                if (await GetAsync($"{resource}/{Uri.EscapeDataString(id)}/object/", $"to read {resource} {id}") is { } found)
                {
                    objects.Add(found);
                }
            }

            return objects;
        }

        /// <summary>A single object, such as a settings endpoint's, or null with its problem kept.</summary>
        public Task<JsonNode?> ObjectAsync(string path, string action) => GetAsync(path, action);

        private async Task<JsonNode?> GetAsync(string path, string action) =>
            Kept(await http.SendForJsonAsync(new HttpRequestMessage(HttpMethod.Get, path), action, cancellationToken));

        /// <summary>The value, or null with its problems kept for the end of the read.</summary>
        private T? Kept<T>(Result<T> result)
            where T : class =>
            result.Match<T?>(
                found => found,
                problems =>
                {
                    Problems.AddRange(problems);
                    return null;
                });
    }
}
