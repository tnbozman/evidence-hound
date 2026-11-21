using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>One write of a plan: the change, the object the scope declares (null for a delete) and the root folder domains sit under.</summary>
public sealed record ScopeWrite(ScopeChange Change, ScopeObject? Declared, string RootFolder);

/// <summary>Carries out one change in CISO Assistant, finding the IDs of the objects it refers to by name.</summary>
public interface IScopeWriter
{
    Task<Result<Unit>> WriteAsync(ScopeWrite write, CancellationToken cancellationToken);
}

public enum ApplyOutcome
{
    Written,
    Failed,
    Skipped,
}

/// <summary>What happened to one change: written, failed (with the problem), or skipped because something it depends on was not written.</summary>
public sealed record AppliedChange(ScopeChange Change, ApplyOutcome Outcome, string Detail);

/// <summary>Each change the apply handled, in order, and how many changes it left for a later version (kinds it does not apply yet).</summary>
public sealed record ScopeApplyReport(IReadOnlyList<AppliedChange> Changes, int NotApplied)
{
    public int Written => Changes.Count(c => c.Outcome == ApplyOutcome.Written);

    public int Failed => Changes.Count(c => c.Outcome == ApplyOutcome.Failed);

    public int Skipped => Changes.Count(c => c.Outcome == ApplyOutcome.Skipped);
}

/// <summary>Carries out a scope plan through an <see cref="IScopeWriter"/>.</summary>
public static class ScopeApplier
{
    public static Task<ScopeApplyReport> ApplyAsync(
        ScopePlan plan, IReadOnlyList<ScopeObject> declared, IReadOnlyCollection<ScopeKind> kinds, string rootFolder, IScopeWriter writer, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
