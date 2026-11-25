using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>One scope file: its path as shown in problems, and its text.</summary>
public sealed record ScopeSource(string Path, string Text);

/// <summary>What parsing found: the scope as far as it could be read, and every problem in every file.</summary>
public sealed record ScopeParse(ScopeDocument Scope, IReadOnlyList<ScopeProblem> Problems);

/// <summary>
/// Parses a compliance repo's scope files (see <c>schemas/scope.schema.json</c>). Every problem names the file and line. Parsing goes
/// on past a problem, so one run reports everything; an item missing a required key is left out of the scope.
/// </summary>
public static partial class ScopeLoader
{
    private static readonly string[] SetupKeys = ["organisation", "frameworks", "libraries", "domains", "programmes"];
    private static readonly string[] ProgrammeKeys = ["id", "name", "framework", "groups", "audit", "group_by", "register"];
    private static readonly string[] RegisterKeys = ["of", "by"];
    private static readonly string[] GroupBys = ["parent"];
    private static readonly string[] OrganisationKeys = ["root_folder", "entity"];
    private static readonly string[] FrameworkKeys = ["urn", "library", "file", "snapshot"];
    private static readonly string[] LibraryFileKeys = ["urn", "file", "snapshot"];
    private static readonly string[] DomainKeys = ["name", "description", "perimeters", "perimeter", "systems"];
    private static readonly string[] PerimeterKeys = ["name", "ref_id", "description"];
    private static readonly string[] SystemKeys = ["name", "ref_id", "type", "description", "owner", "parents", "tags", "solution", "supplied_through", "data", "ai", "objectives", "recovery"];
    private static readonly string[] ControlsKeys = ["controls"];
    private static readonly string[] ControlKeys =
        ["ref_id", "name", "description", "domain", "owner", "status", "priority", "category", "eta", "systems", "reference_control", "documents", "responsible_entity"];

    private static readonly string[] AuditKeys =
    [
        "ref_id", "name", "framework", "groups", "domain", "perimeter", "eta", "due_date", "authors", "reviewers", "field_visibility",
        "systems", "documents", "controls", "assessments", "cycle", "sign_offs", "unlisted",
    ];

    private static readonly string[] FieldAccessKeys = ["auditor", "respondent"];
    private static readonly string[] Accesses = ["edit", "read", "hidden"];
    private static readonly string[] DocumentKeys = ["id", "name", "files", "systems", "requirements"];
    private static readonly string[] AuditControlKeys = ["ref_id", "requirements"];
    private static readonly string[] AssessmentKeys = ["ref", "urn", "status", "result", "observation", "provided_by"];
    private static readonly string[] Statuses = ["to_do", "in_progress", "in_review", "done"];
    private static readonly string[] CycleKeys = ["period", "starts", "close"];
    private static readonly string[] Closes = ["sign_off", "auto"];
    private static readonly string[] Periods = [.. AuditPeriods.Lengths];
    private static readonly string[] SignOffKeys = ["period", "by", "date"];
    private static readonly string[] UnlistedKeys = ["result", "reason"];
    private static readonly string[] SystemTypes = ["primary", "supporting"];
    private static readonly string[] ControlStatuses = ["to_do", "in_progress", "on_hold", "active", "degraded", "deprecated", "--"];
    private static readonly string[] AccessKeys = ["custom_roles", "users", "teams", "roles"];
    private static readonly string[] UserKeys = ["email", "first_name", "last_name", "active", "service_account"];
    private static readonly string[] TeamKeys = ["name", "description", "domain", "leader", "members", "email"];
    private static readonly string[] RoleKeys = ["role", "domain", "users", "teams"];
    private static readonly string[] Booleans = ["true", "false"];
    private static readonly string[] ServiceAccounts = ["true", "publish", "apply"];
    private static readonly string[] SettingsKeys = ["feature_flags", "general"];

    /// <summary>Keys that would hold a credential: rejected wherever they appear in <c>access.yaml</c>, without echoing the value.</summary>
    private static readonly string[] SecretKeys = ["password", "password_hash", "api_token", "token", "secret", "totp_secret"];

    public static ScopeParse Parse(
        ScopeSource setup, ScopeSource? controls, IReadOnlyList<ScopeSource> audits, ScopeSource? access = null, ScopeSource? settings = null, IReadOnlyList<ScopeSource>? thirdParties = null,
        ScopeSource? capabilities = null, ScopeSource? evidencePlan = null, ScopeSource? ssp = null, IReadOnlyList<ScopeSource>? risks = null, ScopeSource? exceptions = null, IReadOnlyList<ScopeSource>? bias = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(audits);

        var setupFile = new FileReader(setup);
        var parsedSetup = setupFile.Root() is { } setupRoot ? ReadSetup(setupRoot, setupFile) : new ScopeSetup("Global", [], []);
        var controlsFile = controls is null ? null : new FileReader(controls);
        var parsedControls = controlsFile?.Root() is { } controlsRoot ? ReadControls(controlsRoot, controlsFile) : [];
        var auditFiles = audits.Select(a => new FileReader(a)).ToArray();
        var parsedAudits = auditFiles.Select(file => file.Root() is { } root ? ReadAudit(root, file) : null).OfType<ScopeAudit>().ToArray();
        var accessFile = access is null ? null : new FileReader(access);
        var parsedAccess = accessFile?.Root() is { } accessRoot ? ReadAccess(accessRoot, accessFile) : ScopeAccess.None;
        var settingsFile = settings is null ? null : new FileReader(settings);
        var parsedSettings = settingsFile?.Root() is { } settingsRoot ? ReadSettings(settingsRoot, settingsFile) : ScopeSettings.None;
        var thirdPartyFiles = (thirdParties ?? []).Select(t => new FileReader(t)).ToArray();
        var parsedThirdParties = thirdPartyFiles.Select(file => file.Root() is { } root ? ReadEntity(root, file) : null).OfType<ScopeEntity>().ToArray();
        var capabilitiesFile = capabilities is null ? null : new FileReader(capabilities);
        var parsedCapabilities = capabilitiesFile?.Root() is { } capabilitiesRoot ? ReadCapabilities(capabilitiesRoot, capabilitiesFile) : null;
        var planFile = evidencePlan is null ? null : new FileReader(evidencePlan);
        var parsedPlan = planFile?.Root() is { } planRoot ? ReadEvidencePlan(planRoot, planFile) : null;
        var sspFile = ssp is null ? null : new FileReader(ssp);
        var parsedSsps = sspFile?.Root() is { } sspRoot ? ReadSsps(sspRoot, sspFile) : [];
        var riskFiles = (risks ?? []).Select(r => new FileReader(r)).ToArray();
        var parsedRisks = riskFiles.Select(file => file.Root() is { } root ? ReadRiskStudy(root, file) : null).OfType<ScopeRiskStudy>().ToArray();
        var biaFiles = (bias ?? []).Select(b => new FileReader(b)).ToArray();
        var parsedBias = biaFiles.Select(file => file.Root() is { } root ? ReadBia(root, file) : null).OfType<ScopeBia>().ToArray();
        var exceptionsFile = exceptions is null ? null : new FileReader(exceptions);
        var parsedExceptions = exceptionsFile?.Root() is { } exceptionsRoot ? ReadExceptions(exceptionsRoot, exceptionsFile) : [];

        return new ScopeParse(
            new ScopeDocument(parsedSetup, parsedControls, parsedAudits) { Access = parsedAccess, Settings = parsedSettings, ThirdParties = parsedThirdParties, Capabilities = parsedCapabilities, EvidencePlan = parsedPlan, Ssps = parsedSsps, RiskStudies = parsedRisks, Exceptions = parsedExceptions, Bias = parsedBias },
            [
                .. setupFile.Problems, .. controlsFile?.Problems ?? [], .. auditFiles.SelectMany(f => f.Problems), .. accessFile?.Problems ?? [],
                .. settingsFile?.Problems ?? [], .. thirdPartyFiles.SelectMany(f => f.Problems), .. capabilitiesFile?.Problems ?? [], .. planFile?.Problems ?? [], .. sspFile?.Problems ?? [],
                .. riskFiles.SelectMany(f => f.Problems), .. exceptionsFile?.Problems ?? [], .. biaFiles.SelectMany(f => f.Problems),
            ]);
    }

    /// <summary>Feature flags as true or false, and general settings as any YAML value, kept as JSON. Names are checked by validation.</summary>
    private static ScopeSettings ReadSettings(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, SettingsKeys);
        var flags = file.Map(root, "feature_flags") is { } flagMap
            ? flagMap.Children.Select(pair => ReadFlag(pair.Key, pair.Value, file)).OfType<ScopeFlag>().ToArray()
            : [];
        var general = file.Map(root, "general") is { } generalMap ? generalMap : null;
        var secrets = general is null ? [] : CisoAssistantSettings.Current.SecretGeneral.Concat(SecretKeys).ToArray();
        if (general is not null)
        {
            file.Secrets(general, secrets);
        }

        return new ScopeSettings(
            flags,
            general is null
                ? []
                : [.. general.Children
                    .Where(pair => pair.Key is YamlScalarNode { Value: { } name } && !secrets.Contains(name, StringComparer.Ordinal))
                    .Select(pair => new ScopeGeneralSetting(((YamlScalarNode)pair.Key).Value!, CisoAssistantSettings.Json(ToJson(pair.Value)), file.At(pair.Key)))]);
    }

    private static ScopeFlag? ReadFlag(YamlNode key, YamlNode value, FileReader file)
    {
        if (key is not YamlScalarNode { Value: { } name })
        {
            return null;
        }

        if (value is YamlScalarNode { Style: ScalarStyle.Plain, Value: "true" or "false" } flag)
        {
            return new ScopeFlag(name, flag.Value == "true", file.At(key));
        }

        file.Invalid(value, $"feature flag '{name}' must be true or false.");
        return null;
    }

    /// <summary>A YAML value as JSON: unquoted true, false, numbers and null keep their type; everything else is text.</summary>
    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", ToJson(c.Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(ToJson)]),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: "true" or "false" } scalar => JsonValue.Create(scalar.Value == "true"),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "null" or "~" } => null,
        YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) => JsonValue.Create(whole),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
        YamlScalarNode scalar => JsonValue.Create(scalar.Value),
        _ => null,
    };

    private static ScopeAccess ReadAccess(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, AccessKeys);
        var users = new List<ScopeUser>();
        foreach (var user in file.Items(root, "users").Select(item => ReadUser(item, file)).OfType<ScopeUser>())
        {
            if (users.Find(u => u.Email == user.Email) is { } first)
            {
                file.Problem(
                    "access.email_duplicate",
                    user.At,
                    $"'{user.Email}' is already listed at line {first.At.Line} (emails are compared without case).",
                    "List each person once.");
                continue;
            }

            users.Add(user);
        }

        return new ScopeAccess(
            users,
            [.. file.Items(root, "teams").Select(item => ReadTeam(item, file)).OfType<ScopeTeam>()],
            [.. file.Items(root, "roles").Select(item => ReadRoleGrant(item, file)).OfType<ScopeRoleGrant>()])
        {
            CustomRoles = file.Refs(root, "custom_roles"),
        };
    }

    private static ScopeUser? ReadUser(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, [.. UserKeys, .. SecretKeys]);
        file.Secrets(item, SecretKeys);
        var email = file.Email(item, "email", required: true);
        var active = file.OneOf(item, "active", Booleans, required: false);
        return email is null
            ? null
            : new ScopeUser(email.Value, file.Text(item, "first_name"), file.Text(item, "last_name"), active != "false", file.OneOf(item, "service_account", ServiceAccounts, required: false), email.At);
    }

    private static ScopeTeam? ReadTeam(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, [.. TeamKeys, .. SecretKeys]);
        file.Secrets(item, SecretKeys);
        var name = file.Required(item, "name");
        return name is null
            ? null
            : new ScopeTeam(name, file.Text(item, "description"), file.Ref(item, "domain"), file.Email(item, "leader"), file.Emails(item, "members"), file.At(item))
            {
                Email = file.Email(item, "email")?.Value,
            };
    }

    private static ScopeRoleGrant? ReadRoleGrant(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, RoleKeys);
        var role = file.Ref(item, "role", required: true);
        var domain = file.Ref(item, "domain", required: true);
        return role is null || domain is null ? null : new ScopeRoleGrant(role, domain, file.Emails(item, "users"), file.Refs(item, "teams"), file.At(item));
    }

    private static ScopeSetup ReadSetup(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, SetupKeys);
        var organisation = file.Map(root, "organisation");
        if (organisation is not null)
        {
            file.Keys(organisation, OrganisationKeys);
        }

        return new ScopeSetup(
            (organisation is null ? null : file.Text(organisation, "root_folder")) ?? "Global",
            [.. file.Items(root, "frameworks").Select(item => ReadFramework(item, file)).OfType<ScopeFramework>()],
            [.. file.Items(root, "domains").Select(item => ReadDomain(item, file)).OfType<ScopeDomain>()])
        {
            Libraries = [.. file.Entries(root, "libraries").OfType<YamlScalarNode>().Select(item => file.Ref(item)).OfType<ScopeRef>()],
            LibraryFiles = [.. file.Entries(root, "libraries").OfType<YamlMappingNode>().Select(item => ReadLibraryFile(item, file)).OfType<ScopeLibraryFile>()],
            Organisation = organisation is not null && file.Map(organisation, "entity") is { } entity ? ReadOrganisation(entity, file) : null,
            Programmes = [.. file.Items(root, "programmes").Select(item => ReadProgramme(item, file)).OfType<ScopeProgramme>()],
        };
    }

    private static ScopeProgramme? ReadProgramme(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ProgrammeKeys);
        var id = file.Required(item, "id");
        var name = file.Required(item, "name");
        var framework = file.Ref(item, "framework", required: true);
        var audit = file.Ref(item, "audit", required: true);
        var groupBy = file.OneOf(item, "group_by", GroupBys, required: false) ?? "parent";
        return id is null || name is null || framework is null || audit is null
            ? null
            : new ScopeProgramme(id, name, framework, [.. file.Refs(item, "groups").Select(g => g.Value)], audit, groupBy, file.At(item))
            {
                Register = file.Map(item, "register") is { } register ? ReadRegister(register, file) : null,
            };
    }

    private static ProgrammeRegister? ReadRegister(YamlMappingNode map, FileReader file)
    {
        file.Keys(map, RegisterKeys);
        var of = file.OneOf(map, "of", [.. RegisterItems.Names], required: true);
        var by = file.Refs(map, "by");
        if (of is null)
        {
            return null;
        }

        var fields = RegisterItems.Fields(of);
        foreach (var unknown in by.Where(b => !fields.Contains(b.Value, StringComparer.Ordinal)))
        {
            file.Problem(
                "scope.programme_register_unknown",
                unknown.At,
                $"the {of} register has no field '{unknown.Value}' to count by.",
                $"Use {string.Join(", ", fields)}.");
        }

        return new ProgrammeRegister(of, [.. by.Select(b => b.Value)], file.At(map));
    }

    private static ScopeFramework? ReadFramework(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, FrameworkKeys);
        var urn = file.Required(item, "urn");
        return urn is null ? null : new ScopeFramework(urn, file.Text(item, "library"), file.Text(item, "file"), file.Text(item, "snapshot"), file.At(item));
    }

    /// <summary>A custom library in the repo (s15-01): its URN and file, and optionally its snapshot.</summary>
    private static ScopeLibraryFile? ReadLibraryFile(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, LibraryFileKeys);
        var urn = file.Required(item, "urn");
        var path = file.Required(item, "file");
        return urn is null || path is null ? null : new ScopeLibraryFile(urn, path, file.Text(item, "snapshot"), file.At(item));
    }

    private static ScopeDomain? ReadDomain(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, DomainKeys);
        var name = file.Required(item, "name");
        var perimeters = FileReader.Child(item, "perimeter") is YamlMappingNode single
            ? [single]
            : file.Items(item, "perimeters");
        return name is null
            ? null
            : new ScopeDomain(
                name,
                file.Text(item, "description"),
                [.. perimeters.Select(p => ReadPerimeter(p, file)).OfType<ScopePerimeter>()],
                [.. file.Items(item, "systems").Select(s => ReadSystem(s, file)).OfType<ScopeSystem>()],
                file.At(item));
    }

    private static ScopePerimeter? ReadPerimeter(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, PerimeterKeys);
        var name = file.Required(item, "name");
        return name is null ? null : new ScopePerimeter(name, file.Text(item, "ref_id"), file.Text(item, "description"), file.At(item));
    }

    private static ScopeSystem? ReadSystem(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, SystemKeys);
        var name = file.Required(item, "name");
        var type = file.OneOf(item, "type", SystemTypes, required: true);
        var tags = file.Map(item, "tags");
        return name is null || type is null
            ? null
            : new ScopeSystem(
                name,
                file.Text(item, "ref_id"),
                type == "primary" ? SystemType.Primary : SystemType.Supporting,
                file.Text(item, "description"),
                file.Text(item, "owner"),
                file.Refs(item, "parents"),
                tags is null ? new Dictionary<string, string>() : file.TextMap(tags),
                file.At(item))
            {
                Solution = file.Ref(item, "solution"),
                SuppliedThrough = file.Ref(item, "supplied_through"),
                Data = ReadData(item, file, authority: false),
                Ai = ReadAi(item, name, file),
                Objectives = ReadObjectives(item, file),
                Recovery = ReadRecovery(item, file),
            };
    }

    private static ScopeControl[] ReadControls(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, ControlsKeys);
        return [.. file.Items(root, "controls").Select(item => ReadControl(item, file)).OfType<ScopeControl>()];
    }

    private static ScopeControl? ReadControl(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ControlKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        return refId is null || name is null
            ? null
            : new ScopeControl(
                refId,
                name,
                file.Text(item, "description"),
                file.Ref(item, "domain"),
                file.Text(item, "owner"),
                file.OneOf(item, "status", ControlStatuses, required: false),
                file.Text(item, "priority"),
                file.Text(item, "category"),
                file.Text(item, "eta"),
                file.Refs(item, "systems"),
                file.Text(item, "reference_control"),
                file.Refs(item, "documents"),
                file.At(item))
            {
                ResponsibleEntity = file.OneOf(item, "responsible_entity", SspAnnex.ResponsibleEntities, required: false),
            };
    }

    private static ScopeAudit? ReadAudit(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, AuditKeys);
        var refId = file.Required(root, "ref_id");
        var name = file.Required(root, "name");
        var framework = file.Ref(root, "framework", required: true);
        var domain = file.Ref(root, "domain", required: true);
        var perimeter = file.Ref(root, "perimeter", required: true);
        var groups = file.Refs(root, "groups");
        var fieldVisibility = file.Map(root, "field_visibility") is { } visibility ? ReadFieldVisibility(visibility, file) : new Dictionary<string, FieldAccess>();
        var documents = file.Items(root, "documents").Select(d => ReadDocument(d, file)).OfType<ScopeEvidenceDocument>().ToArray();
        var controls = file.Items(root, "controls").Select(c => ReadAuditControl(c, file)).OfType<AuditControl>().ToArray();
        var assessments = file.Items(root, "assessments").Select(a => ReadAssessment(a, file)).OfType<HandAssessment>().ToArray();
        var cycle = file.Map(root, "cycle") is { } cycleNode ? ReadCycle(cycleNode, file) : null;
        var signOffs = file.Items(root, "sign_offs").Select(s => ReadSignOff(s, file)).OfType<SignOff>().ToArray();
        var unlisted = file.Map(root, "unlisted") is { } unlistedNode ? ReadUnlisted(unlistedNode, file) : null;

        return refId is null || name is null || framework is null || domain is null || perimeter is null
            ? null
            : new ScopeAudit(refId, name, framework, groups, domain, perimeter, file.At(root))
            {
                Eta = file.Text(root, "eta"),
                DueDate = file.Text(root, "due_date"),
                Authors = [.. file.Refs(root, "authors").Select(r => r.Value)],
                Reviewers = [.. file.Refs(root, "reviewers").Select(r => r.Value)],
                FieldVisibility = fieldVisibility,
                Systems = file.Refs(root, "systems"),
                Documents = documents,
                Controls = controls,
                Assessments = assessments,
                Cycle = cycle,
                SignOffs = signOffs,
                Unlisted = unlisted,
            };
    }

    private static Dictionary<string, FieldAccess> ReadFieldVisibility(YamlMappingNode map, FileReader file)
    {
        var fields = new Dictionary<string, FieldAccess>(StringComparer.Ordinal);
        foreach (var (key, value) in map.Children)
        {
            if (value is not YamlMappingNode access)
            {
                file.Invalid(value, $"'{key}' must be a map of auditor and respondent access.");
                continue;
            }

            file.Keys(access, FieldAccessKeys);
            fields[((YamlScalarNode)key).Value ?? ""] = new FieldAccess(
                file.OneOf(access, "auditor", Accesses, required: false), file.OneOf(access, "respondent", Accesses, required: false), file.At(key));
        }

        return fields;
    }

    private static ScopeEvidenceDocument? ReadDocument(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, DocumentKeys);
        var id = file.Required(item, "id");
        var name = file.Required(item, "name");
        return id is null || name is null
            ? null
            : new ScopeEvidenceDocument(id, name, [.. file.Refs(item, "files").Select(f => f.Value)], file.Refs(item, "systems"), Requirements(file.Refs(item, "requirements")), file.At(item));
    }

    private static AuditControl? ReadAuditControl(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, AuditControlKeys);
        var control = file.Ref(item, "ref_id", required: true);
        return control is null ? null : new AuditControl(control, Requirements(file.Refs(item, "requirements")));
    }

    private static HandAssessment? ReadAssessment(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, AssessmentKeys);
        var byRef = file.Ref(item, "ref");
        var byUrn = file.Ref(item, "urn");
        if ((byRef is null) == (byUrn is null))
        {
            file.Invalid(item, "an assessment names its requirement with exactly one of 'ref' or 'urn'.");
        }

        var status = file.OneOf(item, "status", Statuses, required: true);
        var result = Result(file, item, "result", Enum.GetValues<ComplianceResult>());
        var requirement = byUrn is not null
            ? new RequirementRef(RequirementKey.Urn(byUrn.Value), byUrn.At)
            : byRef is not null ? new RequirementRef(RequirementKey.Ref(byRef.Value), file.At(item)) : null;
        return requirement is null || status is null || result is null
            ? null
            : new HandAssessment(requirement with { At = file.At(item) }, status, result.Value, file.Text(item, "observation") ?? "")
            {
                ProvidedBy = [.. file.Refs(item, "provided_by").Select(r => r.Value)],
            };
    }

    private static AuditCycle? ReadCycle(YamlMappingNode map, FileReader file)
    {
        file.Keys(map, CycleKeys);
        var period = file.OneOf(map, "period", Periods, required: true);
        var starts = file.Required(map, "starts");
        var close = file.OneOf(map, "close", Closes, required: false) ?? "sign_off";
        return period is null || starts is null ? null : new AuditCycle(period, starts, file.At(map)) { Close = close };
    }

    private static SignOff? ReadSignOff(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, SignOffKeys);
        var period = file.Required(item, "period");
        var by = file.Required(item, "by");
        var date = file.Required(item, "date");
        return period is null || by is null || date is null ? null : new SignOff(period, by, date) { At = file.At(item) };
    }

    private static UnlistedRule? ReadUnlisted(YamlMappingNode map, FileReader file)
    {
        file.Keys(map, UnlistedKeys);
        var result = Result(file, map, "result", [ComplianceResult.NotAssessed, ComplianceResult.NotApplicable]);
        return result is null ? null : new UnlistedRule(result.Value, file.Text(map, "reason"), file.At(map));
    }

    private static ComplianceResult? Result(FileReader file, YamlMappingNode map, string key, ComplianceResult[] allowed)
    {
        var name = file.OneOf(map, key, [.. allowed.Select(Names.Of)], required: true);
        return name is null ? null : allowed.Single(r => Names.Of(r) == name);
    }

    private static RequirementRef[] Requirements(IReadOnlyList<ScopeRef> refs) =>
        [.. refs.Select(r => new RequirementRef(r.Value.StartsWith("urn:", StringComparison.Ordinal) ? RequirementKey.Urn(r.Value) : RequirementKey.Ref(r.Value), r.At))];

    /// <summary>Reads one file and collects its problems.</summary>
    private sealed class FileReader(ScopeSource source)
    {
        private readonly List<ScopeProblem> _problems = [];

        public IReadOnlyList<ScopeProblem> Problems => _problems;

        public YamlMappingNode? Root()
        {
            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(source.Text));
            }
            catch (YamlException ex) when (ex.Message.Contains("Duplicate key", StringComparison.Ordinal))
            {
                Add("scope.key_duplicate", ex.Start, "a key appears twice in the same map.", "Remove or rename one of them.");
                return null;
            }
            catch (YamlException ex)
            {
                Add("scope.yaml_invalid", ex.Start, $"not valid YAML: {ex.Message}", "Fix the YAML syntax at this line.");
                return null;
            }

            switch (stream.Documents.FirstOrDefault()?.RootNode)
            {
                case YamlMappingNode root:
                    return root;
                case var other:
                    Add("scope.document_not_a_map", other?.Start ?? Mark.Empty, "the file must be a map of keys.", "See schemas/scope.schema.json for the layout.");
                    return null;
            }
        }

        public SourceLine At(YamlNode node) => new(source.Path, (int)Math.Max(node.Start.Line, 1));

        public void Keys(YamlMappingNode map, string[] allowed)
        {
            foreach (var key in map.Children.Keys.Where(k => k is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal)))
            {
                Add("scope.key_unknown", key.Start, $"unknown key '{key}'.", $"Expected one of: {string.Join(", ", allowed)}.");
            }
        }

        public static YamlNode? Child(YamlMappingNode map, string key) =>
            map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

        public string? Text(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => null,
            YamlScalarNode { Value: { Length: > 0 } value } => value,
            var other => Invalid(other, $"'{key}' must be a non-empty text value."),
        };

        public string? Required(YamlMappingNode map, string key)
        {
            if (Child(map, key) is null)
            {
                Add("scope.key_missing", map.Start, $"'{key}' is missing.", $"Add '{key}'.");
                return null;
            }

            return Text(map, key);
        }

        public string? OneOf(YamlMappingNode map, string key, string[] allowed, bool required)
        {
            var value = required ? Required(map, key) : Text(map, key);
            return value is null || allowed.Contains(value, StringComparer.Ordinal)
                ? value
                : Invalid(Child(map, key)!, $"'{key}' is '{value}'; expected one of {string.Join(", ", allowed)}.");
        }

        public ScopeRef? Ref(YamlMappingNode map, string key, bool required = false) =>
            (required ? Required(map, key) : Text(map, key)) is { } value ? new ScopeRef(value, At(Child(map, key)!)) : null;

        public IReadOnlyList<ScopeRef> Refs(YamlMappingNode map, string key) =>
            [.. Sequence(map, key).Select(item => item is YamlScalarNode { Value: { Length: > 0 } value } ? new ScopeRef(value, At(item)) : Reject<ScopeRef>(item, $"each item of '{key}' must be a text value."))
                .OfType<ScopeRef>()];

        /// <summary>Each item of a list that may mix text values and maps; any other item is reported.</summary>
        public YamlNode[] Entries(YamlMappingNode map, string key) =>
            [.. Sequence(map, key).Where(item => item is YamlScalarNode { Value.Length: > 0 } or YamlMappingNode || Reject<YamlNode>(item, $"each item of '{key}' must be a URN or a map.") is not null)];

        /// <summary>A text value as a reference, with where it was written.</summary>
        public ScopeRef? Ref(YamlScalarNode node) => node.Value is { Length: > 0 } value ? new ScopeRef(value, At(node)) : null;

        public YamlMappingNode? Map(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => null,
            YamlMappingNode child => child,
            var other => Reject<YamlMappingNode>(other, $"'{key}' must be a map."),
        };

        public YamlMappingNode[] Items(YamlMappingNode map, string key) =>
            [.. Sequence(map, key).Select(item => item as YamlMappingNode ?? Reject<YamlMappingNode>(item, $"each item of '{key}' must be a map.")).OfType<YamlMappingNode>()];

        public Dictionary<string, string> TextMap(YamlMappingNode map) =>
            map.Children
                .Select(pair => (Key: (pair.Key as YamlScalarNode)?.Value, Value: pair.Value is YamlScalarNode { Value: { } text } ? text : Invalid(pair.Value, "each value must be text.")))
                .Where(pair => pair.Key is not null && pair.Value is not null)
                .ToDictionary(pair => pair.Key!, pair => pair.Value!, StringComparer.Ordinal);

        public string? Invalid(YamlNode node, string message) => Reject<string>(node, message);

        public void Problem(string code, SourceLine at, string message, string hint) => _problems.Add(new ScopeProblem(code, at, message, hint));

        /// <summary>Reports each credential key in <paramref name="map"/>, naming the key and never its value.</summary>
        public void Secrets(YamlMappingNode map, string[] secretKeys)
        {
            foreach (var key in map.Children.Keys.OfType<YamlScalarNode>().Where(k => secretKeys.Contains(k.Value, StringComparer.Ordinal)))
            {
                Add(
                    "scope.secret_in_file",
                    key.Start,
                    $"'{key.Value}' holds a credential, which must never be in a scope file (its value is not shown).",
                    "Remove it, and rotate it if it was ever committed. People set their own password from CISO Assistant's invitation email; "
                    + "a service account's token is issued into a secrets file (deploy/compose/runner-token.sh).");
            }
        }

        /// <summary>An email address, in lower case, so references to it match whatever case they are written in.</summary>
        public ScopeRef? Email(YamlMappingNode map, string key, bool required = false) =>
            Ref(map, key, required) is { } email ? Checked(email, Child(map, key)!) : null;

        public IReadOnlyList<ScopeRef> Emails(YamlMappingNode map, string key) =>
            [.. Sequence(map, key).Select(item => item is YamlScalarNode { Value: { Length: > 0 } value }
                    ? Checked(new ScopeRef(value, At(item)), item)
                    : Reject<ScopeRef>(item, $"each item of '{key}' must be an email address."))
                .OfType<ScopeRef>()];

        private ScopeRef? Checked(ScopeRef email, YamlNode node)
        {
            if (EmailShape().IsMatch(email.Value))
            {
                return email with { Value = email.Value.ToLowerInvariant() };
            }

            Add("access.email_invalid", node.Start, $"'{email.Value}' is not an email address.", "Write the address the person signs in to CISO Assistant with, such as it@example.com.");
            return null;
        }

        /// <summary>Reports a value of the wrong shape and gives null, so the caller leaves it out.</summary>
        private T? Reject<T>(YamlNode node, string message)
            where T : class
        {
            Add("scope.value_invalid", node.Start, message, "See schemas/scope.schema.json for the allowed values.");
            return null;
        }

        private YamlNode[] Sequence(YamlMappingNode map, string key)
        {
            switch (Child(map, key))
            {
                case null:
                    return [];
                case YamlSequenceNode list:
                    return [.. list.Children];
                case var other:
                    Reject<YamlNode>(other, $"'{key}' must be a list.");
                    return [];
            }
        }

        private void Add(string code, Mark mark, string message, string hint) =>
            _problems.Add(new ScopeProblem(code, new SourceLine(source.Path, (int)Math.Max(mark.Line, 1)), message, hint));
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();
}
