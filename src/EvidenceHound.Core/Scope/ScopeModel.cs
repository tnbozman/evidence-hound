using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>Where a value was written: the scope file and its line.</summary>
public sealed record SourceLine(string File, int Line);

/// <summary>A name that refers to something declared elsewhere in the scope, with where it was written.</summary>
public sealed record ScopeRef(string Value, SourceLine At);

/// <summary>A requirement named in a scope file, by ref_id or URN.</summary>
public sealed record RequirementRef(RequirementKey Key, SourceLine At);

/// <summary>
/// The organisation's structure as declared in a compliance repo's <c>scope/</c> folder: <c>setup.yaml</c>, <c>controls.yaml</c>
/// and one file per audit under <c>audits/</c>. See <c>schemas/scope.schema.json</c>.
/// </summary>
public sealed record ScopeDocument(ScopeSetup Setup, IReadOnlyList<ScopeControl> Controls, IReadOnlyList<ScopeAudit> Audits);

/// <summary><c>scope/setup.yaml</c>: the root folder, the frameworks to load and the domains.</summary>
public sealed record ScopeSetup(string RootFolder, IReadOnlyList<ScopeFramework> Frameworks, IReadOnlyList<ScopeDomain> Domains)
{
    /// <summary>Other libraries to load by URN, such as the mapping sets between the frameworks.</summary>
    public IReadOnlyList<ScopeRef> Libraries { get; init; } = [];
}

/// <summary>A framework to load: its URN, and its library by URN or as a custom library file. A snapshot makes it checkable offline.</summary>
public sealed record ScopeFramework(string Urn, string? LibraryUrn, string? LibraryFile, string? Snapshot, SourceLine At);

/// <summary>A domain (a CISO Assistant folder) with its perimeters and systems.</summary>
public sealed record ScopeDomain(string Name, string? Description, IReadOnlyList<ScopePerimeter> Perimeters, IReadOnlyList<ScopeSystem> Systems, SourceLine At);

public sealed record ScopePerimeter(string Name, string? RefId, string? Description, SourceLine At);

public enum SystemType
{
    Primary,
    Supporting,
}

/// <summary>A system (a CISO Assistant asset). Tags are per-framework labels such as an Essentials domain or <c>cui: true</c>.</summary>
public sealed record ScopeSystem(
    string Name,
    string? RefId,
    SystemType Type,
    string? Description,
    string? Owner,
    IReadOnlyList<ScopeRef> Parents,
    IReadOnlyDictionary<string, string> Tags,
    SourceLine At);

/// <summary>An applied control, declared once in <c>scope/controls.yaml</c> and used by any number of audits.</summary>
public sealed record ScopeControl(
    string RefId,
    string Name,
    string? Description,
    ScopeRef? Domain,
    string? Owner,
    string? Status,
    string? Priority,
    string? Category,
    string? Eta,
    IReadOnlyList<ScopeRef> Systems,
    string? ReferenceControl,
    IReadOnlyList<ScopeRef> Documents,
    SourceLine At);

/// <summary>How the auditor and respondent roles see one requirement field: <c>edit</c>, <c>read</c> or <c>hidden</c>.</summary>
public sealed record FieldAccess(string? Auditor, string? Respondent, SourceLine At);

/// <summary>Static evidence such as a policy or report: its files, the systems it covers and the requirements it supports.</summary>
public sealed record ScopeEvidenceDocument(string Id, string Name, IReadOnlyList<string> Files, IReadOnlyList<ScopeRef> Systems, IReadOnlyList<RequirementRef> Requirements, SourceLine At);

/// <summary>An applied control this audit relies on, and the requirements it supports in this audit's framework.</summary>
public sealed record AuditControl(ScopeRef Control, IReadOnlyList<RequirementRef> Requirements);

/// <summary>A hand-written result for a requirement no check covers.</summary>
public sealed record HandAssessment(RequirementRef Requirement, string Status, ComplianceResult Result, string Observation);

/// <summary>An audit that runs in periods: how long each period is and when the first starts.</summary>
public sealed record AuditCycle(string Period, string Starts, SourceLine At);

public sealed record SignOff(string Role, string Name);

/// <summary>What happens to in-group requirements the audit file does not list: not assessed, or not applicable with a reason.</summary>
public sealed record UnlistedRule(ComplianceResult Result, string? Reason, SourceLine At);

/// <summary>One compliance assessment, from <c>scope/audits/&lt;audit&gt;.yaml</c>.</summary>
public sealed record ScopeAudit(
    string RefId,
    string Name,
    ScopeRef Framework,
    IReadOnlyList<ScopeRef> Groups,
    ScopeRef Domain,
    ScopeRef Perimeter,
    SourceLine At)
{
    public string? Eta { get; init; }

    public string? DueDate { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public IReadOnlyList<string> Reviewers { get; init; } = [];

    public IReadOnlyDictionary<string, FieldAccess> FieldVisibility { get; init; } = new Dictionary<string, FieldAccess>();

    public IReadOnlyList<ScopeRef> Systems { get; init; } = [];

    public IReadOnlyList<ScopeEvidenceDocument> Documents { get; init; } = [];

    public IReadOnlyList<AuditControl> Controls { get; init; } = [];

    public IReadOnlyList<HandAssessment> Assessments { get; init; } = [];

    public AuditCycle? Cycle { get; init; }

    public IReadOnlyList<SignOff> SignOffs { get; init; } = [];

    /// <summary>Required; null when the file leaves it out, which validation reports.</summary>
    public UnlistedRule? Unlisted { get; init; }
}
