namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks <c>scope/ssp.yaml</c> (s11-02): one plan per programme, and the programme, perimeter, role holders and documents it names exist,
/// with every role named and every section its profile needs. Pure.
/// </summary>
public static class SspValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var programmes = scope.Setup.Programmes.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var perimeters = scope.Setup.Domains.SelectMany(d => d.Perimeters).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var holders = scope.Access.Users.Select(u => u.Email).Concat(scope.Access.Teams.Select(t => t.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documents = scope.Audits.SelectMany(a => a.Documents).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. scope.Ssps.GroupBy(s => s.Programme.Value, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(s => new ScopeProblem(
                "scope.duplicate", s.At, $"ssp for programme '{g.Key}' is declared more than once; first at {g.First().At.File} line {g.First().At.Line}.", "Keep one plan per programme."))),
            .. scope.Ssps.SelectMany(s => Problems(s, programmes, perimeters, holders, documents)),
        ];
    }

    private static IEnumerable<ScopeProblem> Problems(ScopeSsp ssp, HashSet<string> programmes, HashSet<string> perimeters, HashSet<string> holders, HashSet<string> documents) =>
    [
        .. programmes.Contains(ssp.Programme.Value) ? Array.Empty<ScopeProblem>() : [Unknown("ssp.programme_unknown", ssp.Programme, "programme", "Name a programme from 'programmes:' in scope/setup.yaml.")],
        .. perimeters.Contains(ssp.Perimeter.Value) ? Array.Empty<ScopeProblem>() : [Unknown("ssp.perimeter_unknown", ssp.Perimeter, "perimeter", "Name a perimeter declared in scope/setup.yaml: the plan's boundary.")],
        .. SspProfile.Roles.Where(r => ssp.Roles.All(h => h.Role != r)).Select(r => new ScopeProblem(
            "ssp.role_missing", ssp.At, $"the plan for programme '{ssp.Programme.Value}' names no {r}.", $"Add '{r}:' under 'roles:', with a user's email or a team from scope/access.yaml.")),
        .. ssp.Roles.Where(r => !holders.Contains(r.Holder.Value)).Select(r => Unknown(
            "ssp.role_unknown", r.Holder, $"{r.Role} holder", "Name a user (by email) or a team declared in scope/access.yaml.")),
        .. ssp.Documents.Where(d => !documents.Contains(d.Value)).Select(d => Unknown(
            "ssp.document_unknown", d, "document", "Name a document by its id from an audit's 'documents:'.")),
        .. SspProfile.RequiredSections(ssp.Profile).Where(n => ssp.Sections.All(s => s.Name != n)).Select(n => new ScopeProblem(
            "ssp.section_missing",
            ssp.At,
            $"the {ssp.Profile} profile needs a '{n}' section, which the plan for programme '{ssp.Programme.Value}' does not name.",
            $"Write it as a Markdown file in the repo and name it under 'sections:' as '{n}: <file>'.")),
    ];

    private static ScopeProblem Unknown(string code, ScopeRef reference, string kind, string hint) =>
        new(code, reference.At, $"{kind} '{reference.Value}' is not declared.", hint);
}
