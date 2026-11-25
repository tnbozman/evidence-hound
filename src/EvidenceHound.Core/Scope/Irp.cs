using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary><c>scope/irp.yaml</c> (s15-09): the incident response plan's programme, version, role holders and hand-written sections.</summary>
public sealed record ScopeIrp(ScopeRef Programme, string Version, IReadOnlyList<SspRole> Roles, IReadOnlyList<SspSection> Sections, SourceLine At);

/// <summary>A system in the plan, with the sensitive data it handles.</summary>
public sealed record IrpSystem(SspValue Name, IReadOnlyList<SspValue> Data)
{
    /// <summary>Its recovery objectives, such as <c>rto 4h, rpo 1d</c>, from the business impact analysis (s15-04).</summary>
    public SspValue Recovery { get; init; } = new(null, Name.Source);
}

/// <summary>A party to inform: a supplier or a customer, with its contacts and the clauses its contracts carry.</summary>
public sealed record IrpParty(string Kind, SspValue Name, IReadOnlyList<SspValue> Contacts, IReadOnlyList<SspValue> Clauses);

/// <summary>A report an incident may owe, from <c>scope/reporting.yaml</c>, with what triggers it in this scope.</summary>
public sealed record IrpReport(SspValue Name, SspValue ReportTo, SspValue Within, SspValue Trigger, SspValue Reference);

/// <summary>The incident response plan as a model: every value with its source, and the gaps that stop it being complete.</summary>
public sealed record IrpDocument(
    string Programme,
    SspValue Version,
    IReadOnlyList<SspRoleValue> Contacts,
    IReadOnlyList<IrpSystem> Systems,
    IReadOnlyList<IrpParty> Parties,
    IReadOnlyList<IrpReport> Reports,
    IReadOnlyList<SspNamed> Sections,
    IReadOnlyList<SspGap> Gaps);

/// <summary>
/// Builds the incident response plan from the repo (s15-09): contacts from the role holders, systems and their data from
/// <c>setup.yaml</c>, suppliers and customers from <c>third-parties/</c>, and the reports from <c>reporting.yaml</c>. Pure.
/// </summary>
public static class IrpBuilder
{
    public static IrpDocument Build(ScopeIrp irp, ScopeDocument scope, IReadOnlyList<ReportingObligation> reporting, IReadOnlyDictionary<string, string> sectionTexts)
    {
        ArgumentNullException.ThrowIfNull(irp);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(sectionTexts);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).ToArray();
        return new IrpDocument(
            irp.Programme.Value,
            Repo(irp.Version, irp.At),
            [.. IrpLayout.Roles.Select(r => irp.Roles.FirstOrDefault(h => h.Role == r)).OfType<SspRole>().Select(r => new SspRoleValue(r.Role, Repo(r.Holder.Value, r.Holder.At)))],
            [.. systems.Select(System)],
            [.. scope.ThirdParties.Select(Party)],
            [.. reporting.Select(o => Report(o, systems))],
            [.. irp.Sections.Select(s => new SspNamed(s.Name, Repo(s.File, s.At)))],
            Gaps(irp, sectionTexts));
    }

    /// <summary>A role or judgement section the plan lacks, or a section whose file is missing or empty.</summary>
    public static IReadOnlyList<SspGap> Gaps(ScopeIrp irp, IReadOnlyDictionary<string, string> sectionTexts)
    {
        ArgumentNullException.ThrowIfNull(irp);
        ArgumentNullException.ThrowIfNull(sectionTexts);
        return
        [
            .. IrpLayout.Roles.Where(r => irp.Roles.All(h => h.Role != r))
                .Select(r => new SspGap($"role {r}", $"scope/irp.yaml names no {r}: add '{r}:' under 'roles:' with who holds it.")),
            .. IrpLayout.Sections.Select(name => (name, section: irp.Sections.FirstOrDefault(s => s.Name == name))).Select(x => x.section is null
                ? new SspGap($"section {x.name}", $"scope/irp.yaml names no {x.name} section: write it in a Markdown file and add '{x.name}:' under 'sections:'.")
                : string.IsNullOrWhiteSpace(sectionTexts.GetValueOrDefault(x.section.File))
                    ? new SspGap($"section {x.name}", $"{x.section.File} is missing or empty: write the {IrpLayout.SectionTitle(x.name).ToLowerInvariant()} there.")
                    : null).OfType<SspGap>(),
        ];
    }

    private static IrpSystem System(ScopeSystem system) => new(
        Repo(system.Name, system.At),
        [.. system.Data.Select(d => Repo($"{SensitiveData.Code(d.Kind)}{(d.Detail is { } detail ? $" ({detail})" : "")}, {d.Role.ToString().ToLowerInvariant()}", d.At))])
    {
        Recovery = Repo(system.Recovery.Count == 0 ? null : string.Join(", ", system.Recovery.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {r.Value}")), system.At),
    };

    private static IrpParty Party(ScopeEntity entity)
    {
        var customer = RefIdValidator.IsCustomer(entity);
        return new IrpParty(
            customer ? "customer" : "supplier",
            Repo(entity.Name, entity.At),
            [.. entity.Representatives.Select(r => Repo(r.Email, r.At))],
            [.. entity.Contracts.SelectMany(c => c.Obligations).DistinctBy(o => o.Clause).Select(o => Repo(o.Clause, o.At))]);
    }

    private static IrpReport Report(ReportingObligation obligation, IReadOnlyList<ScopeSystem> systems)
    {
        var holding = systems.Where(s => s.Data.Any(d => obligation.Data.Contains(d.Kind))).Select(s => s.Name).ToArray();
        var trigger = obligation.Always
            ? "every incident"
            : $"an incident on a system handling {string.Join(" or ", obligation.Data.Select(SensitiveData.Code))} ({(holding.Length == 0 ? "none in scope" : string.Join(", ", holding))})";
        return new IrpReport(
            Repo(obligation.Name, obligation.At),
            Repo(obligation.ReportTo, obligation.At),
            Repo(Words(obligation.Within), obligation.At),
            Repo(trigger, obligation.At),
            Repo(obligation.Reference, obligation.At));
    }

    /// <summary>A deadline as a reader says it: hours up to a week, then days, such as <c>72 hours</c> or <c>30 days</c>.</summary>
    internal static string Words(Duration within) => within.Seconds switch
    {
        var s when s % 86400 == 0 && s >= 604800 => string.Create(CultureInfo.InvariantCulture, $"{s / 86400} days"),
        var s when s % 3600 == 0 => string.Create(CultureInfo.InvariantCulture, $"{s / 3600} hour{(s == 3600 ? "" : "s")}"),
        var s => string.Create(CultureInfo.InvariantCulture, $"{s / 60} minutes"),
    };

    private static SspValue Repo(string? value, SourceLine at) =>
        new(value, new SspSource(SspSource.Repo, $"{at.File} line {at.Line.ToString(CultureInfo.InvariantCulture)}"));
}
