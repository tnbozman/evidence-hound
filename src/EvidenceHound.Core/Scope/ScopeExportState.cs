using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Everything CISO Assistant holds under a root folder, for <c>hound scope export</c>: the objects as the plan reads them, with every row of every
/// audit, and a snapshot of each framework those audits use.
/// </summary>
public sealed record ScopeExportState(string RootFolder, IReadOnlyList<ActualObject> Objects, IReadOnlyList<FrameworkSnapshot> Frameworks);

/// <summary>Reads what <c>hound scope export</c> writes out. Read-only.</summary>
public interface IScopeExportSource
{
    Task<Result<ScopeExportState>> ReadAllAsync(string rootFolder, CancellationToken cancellationToken);
}

/// <summary>The <c>ref_id</c> given to an audit, control or document that has none, made from its name. Pure.</summary>
public static class ScopeRefIds
{
    public static string For(ScopeKind kind, string name) => "";
}
