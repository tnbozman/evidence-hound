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
            .. scope.Setup.Domains.SelectMany(d => d.Systems)
                .Where(s => s.Tags.TryGetValue(CmmcAssetCategory.Tag, out var category) && CmmcAssetCategory.Name(category) is null)
                .Select(s => new ScopeProblem(
                    "ssp.asset_category_invalid",
                    s.At,
                    $"system '{s.Name}' has {CmmcAssetCategory.Tag} '{s.Tags[CmmcAssetCategory.Tag]}', which is not a CMMC asset category.",
                    $"Use one of {string.Join(", ", CmmcAssetCategory.Values)} (32 CFR 170.19(c)(1), Table 1).")),
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
        .. SspProfile.RequiredSections.Where(n => ssp.Sections.All(s => s.Name != n)).Select(n => new ScopeProblem(
            "ssp.section_missing",
            ssp.At,
            $"the {ssp.Profile} profile needs a '{n}' section, which the plan for programme '{ssp.Programme.Value}' does not name.",
            $"Write it as a Markdown file in the repo and name it under 'sections:' as '{n}: <file>'.")),
        .. ssp.SignOffs.SelectMany(s => SignOffProblems(ssp, s)),
    ];

    /// <summary>A sign-off is the authorising official's, with SHA-256 hashes written as 64 lower-case hex digits.</summary>
    private static IEnumerable<ScopeProblem> SignOffProblems(ScopeSsp ssp, SspSignOff signOff)
    {
        var at = signOff.At ?? ssp.At;
        var official = ssp.Roles.FirstOrDefault(r => r.Role == "authorising_official")?.Holder.Value;
        if (official is not null && !string.Equals(official, signOff.By, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ScopeProblem(
                "ssp.signoff_not_authorising_official", at, $"version {signOff.Version} is signed by '{signOff.By}', who is not the plan's authorising official ('{official}').",
                "Only the authorising official signs the plan (ISM-0047); change 'by' or the role.");
        }

        foreach (var (name, hash) in new[] { ("sha256", signOff.Sha256), ("snapshot", signOff.Snapshot) }.Where(h => h.Item2 is not null && !IsSha256(h.Item2)))
        {
            yield return new ScopeProblem(
                "ssp.signoff_hash_invalid", at, $"version {signOff.Version}'s {name} '{hash}' is not a SHA-256.",
                "Copy the hash 'hound ssp build --snapshot-out' printed: 64 lower-case hex digits.");
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static ScopeProblem Unknown(string code, ScopeRef reference, string kind, string hint) =>
        new(code, reference.At, $"{kind} '{reference.Value}' is not declared.", hint);
}
