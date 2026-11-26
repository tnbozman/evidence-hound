using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads one audit file under <c>scope/audits/</c>: its framework, hand assessments, evidence documents, cycle and sign-offs.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] AuditKeys =
    [
        "ref_id", "name", "framework", "groups", "domain", "perimeter", "eta", "due_date", "authors", "reviewers", "field_visibility",
        "systems", "documents", "controls", "assessments", "cycle", "sign_offs", "unlisted", "no_evidence",
    ];

    private static readonly string[] FieldAccessKeys = ["auditor", "respondent"];

    private static readonly string[] Accesses = ["edit", "read", "hidden"];

    private static readonly string[] DocumentKeys = ["id", "name", "files", "systems", "requirements"];

    private static readonly string[] AuditControlKeys = ["ref_id", "requirements"];

    private static readonly string[] AssessmentKeys = ["ref", "urn", "status", "result", "observation", "provided_by"];

    private static readonly string[] Statuses = ["to_do", "in_progress", "in_review", "done"];

    private static readonly string[] SignOffKeys = ["period", "by", "date"];

    private static readonly string[] UnlistedKeys = ["result", "reason"];

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
                NoEvidence = file.OneOf(root, "no_evidence", ["non_compliant", "not_assessed"], required: false) == "not_assessed" ? ComplianceResult.NotAssessed : ComplianceResult.NonCompliant,
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
}
