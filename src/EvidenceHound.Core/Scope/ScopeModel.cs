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
public sealed record ScopeDocument(ScopeSetup Setup, IReadOnlyList<ScopeControl> Controls, IReadOnlyList<ScopeAudit> Audits)
{
    /// <summary><c>scope/access.yaml</c>: users, teams and role assignments; empty when the repo has no such file.</summary>
    public ScopeAccess Access { get; init; } = ScopeAccess.None;

    /// <summary><c>scope/settings.yaml</c>: feature flags and general settings; empty when the repo has no such file.</summary>
    public ScopeSettings Settings { get; init; } = ScopeSettings.None;

    /// <summary><c>scope/third-parties/*.yaml</c>: suppliers and other third parties, one entity per file.</summary>
    public IReadOnlyList<ScopeEntity> ThirdParties { get; init; } = [];

    /// <summary><c>scope/capabilities.yaml</c>: which system provides each mitigation; null when the repo has no such file.</summary>
    public ScopeCapabilityMap? Capabilities { get; init; }

    /// <summary><c>scope/evidence-plan.yaml</c>: how each control's evidence arrives, from whom and how often; null when the repo has no such file.</summary>
    public ScopeEvidencePlan? EvidencePlan { get; init; }
}

/// <summary><c>scope/setup.yaml</c>: the root folder, the frameworks to load and the domains.</summary>
public sealed record ScopeSetup(string RootFolder, IReadOnlyList<ScopeFramework> Frameworks, IReadOnlyList<ScopeDomain> Domains)
{
    /// <summary>Other libraries to load by URN, such as the mapping sets between the frameworks.</summary>
    public IReadOnlyList<ScopeRef> Libraries { get; init; } = [];

    /// <summary>The organisation's own entity (CISO Assistant's main entity), from <c>organisation.entity</c>. It is only ever updated.</summary>
    public ScopeEntity? Organisation { get; init; }
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
    SourceLine At)
{
    /// <summary>The supplier's solution the system is, from <c>scope/third-parties/</c>; its supplier must be approved before the system is registered.</summary>
    public ScopeRef? Solution { get; init; }

    /// <summary>The reseller or provider the contract is with, when not the solution's own supplier; it must be approved too.</summary>
    public ScopeRef? SuppliedThrough { get; init; }
}

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
public sealed record HandAssessment(RequirementRef Requirement, string Status, ComplianceResult Result, string Observation)
{
    /// <summary>The systems that meet this requirement for the audit, when another system does; the row stays open until their audit shows it met.</summary>
    public IReadOnlyList<string> ProvidedBy { get; init; } = [];
}

/// <summary>
/// An audit that runs in periods: how long each period is, when the first starts, and how an ended period closes: <c>sign_off</c> (locked once
/// the file records its sign-off) or <c>auto</c> (locked when it ends).
/// </summary>
public sealed record AuditCycle(string Period, string Starts, SourceLine At)
{
    public string Close { get; init; } = "sign_off";
}

/// <summary>A period's sign-off, recorded in the audit file so the approval is itself a reviewed commit: the period's label, who signed and when.</summary>
public sealed record SignOff(string Period, string By, string Date)
{
    public SourceLine? At { get; init; }
}

/// <summary>
/// One period of an audit series, as the plan sees it on the day: the series' <c>ref_id</c>, its status (<c>in_progress</c> while open,
/// <c>in_review</c> once ended, <c>done</c> once closed), whether it is locked, and the previous period it was opened from.
/// </summary>
public sealed record PeriodState(string Series, string Status, bool Locked, string? Baseline);

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

    /// <summary>Set on the period audits a series expands into; null for an audit without a cycle.</summary>
    public PeriodState? Period { get; init; }

    /// <summary>Required; null when the file leaves it out, which validation reports.</summary>
    public UnlistedRule? Unlisted { get; init; }
}
