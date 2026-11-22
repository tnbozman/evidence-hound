namespace EvidenceHound.Core.Scope;

/// <summary>
/// A plan saved with <c>hound scope plan --out</c>, for <c>hound scope apply &lt;file&gt;</c> to apply exactly: the plan and its compliance impact,
/// the CISO Assistant it was made against, the day it was made for, the scope folder and config it was made from with a SHA-256 of their files,
/// and, for each object the plan changes or deletes, the fingerprint of the state it read (null when the plan creates it). Never a token or
/// a document's content.
/// </summary>
public sealed record SavedPlan(
    string CisoAssistantUrl,
    string Day,
    string Scope,
    string? Config,
    string InputsSha256,
    string RootFolder,
    ScopePlan Plan,
    IReadOnlyList<AuditImpact> Compliance,
    IReadOnlyDictionary<string, string?> Read);
