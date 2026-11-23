namespace EvidenceHound.Core.Scope;

/// <summary>Checks every ref ID a scope declares against its kind's convention (<see cref="RefIds"/>). Pure.</summary>
public static class RefIdValidator
{
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        IEnumerable<(RefIdKind Kind, string? RefId, SourceLine At)> declared =
        [
            .. scope.Setup.Domains.SelectMany(d => d.Perimeters).Select(p => (RefIdKind.Perimeter, p.RefId, p.At)),
            .. scope.Setup.Domains.SelectMany(d => d.Systems).Select(s => (RefIdKind.System, s.RefId, s.At)),
            .. scope.Controls.Select(c => (RefIdKind.Control, (string?)c.RefId, c.At)),
            .. scope.Audits.Select(a => (RefIdKind.Audit, (string?)a.RefId, a.At)),
            .. scope.ThirdParties.Select(e => (RefIdKind.Supplier, (string?)e.RefId, e.At)),
            .. scope.ThirdParties.SelectMany(e => e.Solutions).Select(s => (RefIdKind.Solution, (string?)s.RefId, s.At)),
            .. scope.ThirdParties.SelectMany(e => e.Contracts).Select(c => (RefIdKind.Contract, (string?)c.RefId, c.At)),
            .. scope.ThirdParties.SelectMany(e => e.Assessments).Select(a => (RefIdKind.EntityAssessment, (string?)a.RefId, a.At)),
        ];
        return declared
            .Where(d => d.RefId is { Length: > 0 } refId && !RefIds.Follows(d.Kind, refId))
            .Select(d => Problem(d.Kind, d.RefId!, d.At));
    }

    private static ScopeProblem Problem(RefIdKind kind, string refId, SourceLine at)
    {
        var convention = RefIds.For(kind);
        return new ScopeProblem(
            "scope.ref_id_convention",
            at,
            kind == RefIdKind.Audit
                ? $"audit ref_id '{refId}' does not read <CODE>-<FRAMEWORK>-<YEAR> in upper case letters, digits and hyphens."
                : $"{convention.Name} ref_id '{refId}' does not follow the convention: {convention.Prefix} then upper case letters, digits and hyphens.",
            kind == RefIdKind.Audit
                ? $"Rename it, such as {convention.Example}, here and wherever the audit is named (hound.yaml targets)."
                : $"Rename it to '{RefIds.Suggest(kind, refId)}', here and wherever it is named.");
    }
}
