using System.Globalization;
using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Builds the system security plan from the scope and one read of CISO Assistant (s11-02). The inventory is the systems the audits on the
/// plan's perimeter cover, as CISO Assistant holds them; external services are the solutions the scope links them to; controls and
/// requirements are the programme audit's rows. Every value carries its source. Pure: the scope must have passed validation.
/// </summary>
public static class SspBuilder
{
    public static SspDocument Build(ScopeSsp ssp, ScopeDocument scope, IReadOnlyList<ActualObject> held, FrameworkSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(ssp);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(held);
        var objects = held.GroupBy(o => (o.Held.Kind, o.Held.Key)).ToDictionary(g => g.Key, g => g.First());
        var programme = scope.Setup.Programmes.Single(p => p.Id == ssp.Programme.Value);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var inBoundary = scope.Audits.Where(a => a.Perimeter.Value == ssp.Perimeter.Value).SelectMany(a => a.Systems).DistinctBy(s => s.Value).ToArray();
        var rows = held.Where(o => o.Held.Kind == ScopeKind.Assessment && o.Held.Key.StartsWith(programme.Audit.Value + "/", StringComparison.Ordinal))
            .OrderBy(o => o.Held.Key, StringComparer.Ordinal)
            .ToArray();
        ActualObject? Find(ScopeKind kind, string? key) => key is not null && objects.TryGetValue((kind, key), out var found) ? found : null;

        SspControl[] controls =
        [
            .. rows.SelectMany(r => Split(r.Held.Fields.GetValueOrDefault("controls"))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(c => Control(c, Find(ScopeKind.Control, c))),
        ];
        var byRef = controls.ToDictionary(c => c.RefId.Value!, StringComparer.Ordinal);

        var document = new SspDocument(
            programme.Id,
            ssp.Profile,
            Repo(ssp.SystemName, ssp.At),
            Repo(ssp.Version, ssp.At),
            Repo(programme.Framework),
            Repo(programme.Audit),
            Repo(ssp.Perimeter),
            [.. ssp.Roles.Select(r => new SspRoleValue(r.Role, Repo(r.Holder)))],
            [.. inBoundary.Select(s => Inventory(s, Find(ScopeKind.System, s.Value)))],
            [.. inBoundary.Select(s => systems.GetValueOrDefault(s.Value)).OfType<ScopeSystem>().Where(s => s.Solution is not null).Select(s => Service(s, Find))],
            controls,
            [.. rows.Select(r => SspStatements.Complete(Requirement(r), byRef))],
            [.. ssp.Documents.Select(d => new SspValue(scope.Audits.SelectMany(a => a.Documents).FirstOrDefault(x => x.Id == d.Value)?.Name ?? d.Value, RepoSource(d.At)))],
            [.. ssp.Sections.Select(s => new SspNamed(s.Name, Repo(s.File, s.At)))])
        {
            Classification = Repo(ssp.Classification, ssp.At),
            OperationalStatus = Repo(ssp.OperationalStatus, ssp.At),
            SystemType = Repo(ssp.SystemType, ssp.At),
        };
        return snapshot is null ? document : document with { Annex = SspAnnex.Of(document, snapshot, programme.Groups) };
    }

    private static SspSystem Inventory(ScopeRef system, ActualObject? held)
    {
        var source = Asset(system.Value);
        return new SspSystem(
            held is null ? Repo(system) : new SspValue(held.Held.Key, source),
            Field(held, "type", source),
            Field(held, "owner", source),
            Field(held, "description", source));
    }

    private static SspService Service(ScopeSystem system, Func<ScopeKind, string?, ActualObject?> find)
    {
        var solutionRef = system.Solution!;
        var solution = find(ScopeKind.Solution, solutionRef.Value);
        var solutionSource = new SspSource(SspSource.CisoAssistant, $"solution {solutionRef.Value}");
        var entityRef = solution?.Held.Fields.GetValueOrDefault("entity");
        var entity = find(ScopeKind.Entity, entityRef);
        return new SspService(
            new SspValue(system.Name, RepoSource(solutionRef.At)),
            Field(solution, "name", solutionSource),
            Field(entity, "name", new SspSource(SspSource.CisoAssistant, $"entity {entityRef ?? "(none)"}")),
            Field(solution, "criticality", solutionSource));
    }

    private static SspControl Control(string refId, ActualObject? held)
    {
        var source = new SspSource(SspSource.CisoAssistant, $"applied control {refId}");
        return new SspControl(
            new SspValue(refId, source),
            Field(held, "name", source),
            Field(held, "owner", source),
            new SspValue(held?.Observed.GetValueOrDefault("status"), source),
            Field(held, "description", source))
        {
            Eta = Field(held, "eta", source),
        };
    }

    private static SspRequirement Requirement(ActualObject row)
    {
        var source = new SspSource(SspSource.CisoAssistant, $"requirement assessment {row.Held.Key}");
        return new SspRequirement(
            new SspValue(row.Held.Key.Split('/', 2)[1], source),
            Field(row, "result", source),
            Field(row, "observation", source),
            Field(row, "documents", source),
            Field(row, "controls", source));
    }

    private static SspValue Field(ActualObject? held, string field, SspSource source) => new(held?.Held.Fields.GetValueOrDefault(field), source);

    private static SspSource Asset(string name) => new(SspSource.CisoAssistant, $"asset '{name}'");

    private static SspValue Repo(ScopeRef reference) => Repo(reference.Value, reference.At);

    private static SspValue Repo(string? value, SourceLine at) => new(value, RepoSource(at));

    private static SspSource RepoSource(SourceLine at) => new(SspSource.Repo, $"{at.File} line {at.Line.ToString(CultureInfo.InvariantCulture)}");

    private static string[] Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
