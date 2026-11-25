using System.Globalization;
using System.Text;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The DFARS 252.204-7012 report pack (s17-07): what a person needs to file the 72-hour cyber incident report on the DoD portal, gathered
/// from the incident in CISO Assistant and the scope. The affected systems that handle CUI with their data kinds and CMMC asset category,
/// the suppliers whose solutions provide them, the customer contracts that carry the clause, the deadline from <c>scope/reporting.yaml</c>
/// (72 hours when it declares none) and the timeline. It files nothing. Pure.
/// </summary>
public static class DfarsReportPack
{
    public static readonly string Clause = "252.204-7012";

    private static readonly Duration Default = new(72 * 3600);

    public static string Markdown(IncidentFacts incident, ScopeDocument scope, IReadOnlyList<ReportingObligation> obligations, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(obligations);
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"# DFARS {Clause} report pack: {incident.RefId} {incident.Name}\n\n");
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).Where(s => incident.Systems.Contains(s.Name, StringComparer.Ordinal)).ToList();
        var cui = systems.Where(s => s.Data.Any(d => d.Kind == SensitiveDataKind.Cui)).ToList();
        if (cui.Count == 0)
        {
            return text.Append(CultureInfo.InvariantCulture, $"No affected system handles CUI under scope/setup.yaml, so no DFARS {Clause} report is owed. ")
                .Append("Check the incident's affected assets in CISO Assistant and each system's data in the scope before you rely on this; ")
                .Append("`hound incident obligations` lists the reports the incident does owe.\n")
                .ToString();
        }

        var obligation = obligations.FirstOrDefault(o => (o.Reference ?? o.Name).Contains(Clause, StringComparison.Ordinal));
        var within = obligation?.Within ?? Default;
        var due = incident.Discovered.AddSeconds(within.Seconds);
        var id = obligation?.Id ?? "dfars-7012";
        var made = incident.Timeline.Any(e => e.Entry.StartsWith(ReportingObligations.ReportedPrefix + id, StringComparison.OrdinalIgnoreCase));
        text.Append("Gathered from CISO Assistant and the scope for the person who files the report on the DoD portal (DIBNet), which needs a ")
            .Append("medium assurance certificate. hound files nothing.\n\n")
            .Append("## Deadline\n\n")
            .Append(CultureInfo.InvariantCulture, $"- Discovered: {Time(incident.Discovered)}\n")
            .Append(CultureInfo.InvariantCulture, $"- Report due: {Time(due)} ({within.Seconds / 3600} hours, {obligation?.Reference ?? $"DFARS {Clause}(c)"}): ")
            .Append(made ? "made" : now > due ? "OVERDUE" : "due").Append('\n')
            .Append(obligation is null ? "- scope/reporting.yaml declares no DFARS obligation, so the 72 hours of the clause apply.\n" : "")
            .Append('\n');

        text.Append("## Affected systems that handle CUI\n\n| System | Data | CMMC asset category | Owner |\n| --- | --- | --- | --- |\n");
        foreach (var s in cui)
        {
            var data = string.Join(", ", s.Data.Select(d => $"{SensitiveData.Code(d.Kind)} ({d.Role.ToString().ToLowerInvariant()}){(d.Detail is { } detail ? $": {detail}" : "")}"));
            var category = s.Tags.GetValueOrDefault(CmmcAssetCategory.Tag) is { } c ? CmmcAssetCategory.Name(c) ?? c : "-";
            text.Append(CultureInfo.InvariantCulture, $"| {s.Name} | {data} | {category} | {s.Owner ?? "-"} |\n");
        }

        Suppliers(text, cui, scope);
        Contracts(text, scope);
        Timeline(text, incident);
        return text.Append("## Before and after you file\n\n")
            .Append("- The portal asks for more than this pack holds, such as your CAGE code, the contracting officer and the technique used. Fill those in on the portal.\n")
            .Append("- Preserve and protect images of the affected systems and the relevant monitoring and packet capture data for at least 90 days from ")
            .Append(CultureInfo.InvariantCulture, $"the report, in case DoD asks for them (DFARS {Clause}(e)).\n")
            .Append(CultureInfo.InvariantCulture, $"- Send any malicious software you isolate to the DoD Cyber Crime Center as the portal directs (DFARS {Clause}(d)); do not email it.\n")
            .Append("- Tell each prime under its contract, as the contracts above require.\n")
            .Append(CultureInfo.InvariantCulture, $"- Record the report as a timeline entry on the incident: '{ReportingObligations.ReportedPrefix}{id}, <DIBNet report number>'.\n")
            .ToString();
    }

    private static void Suppliers(StringBuilder text, IReadOnlyList<ScopeSystem> systems, ScopeDocument scope)
    {
        text.Append("\n## Suppliers of those systems\n\n");
        var rows = systems
            .Where(s => s.Solution is not null)
            .SelectMany(s => scope.ThirdParties
                .Where(e => e.Solutions.Any(x => x.RefId == s.Solution!.Value))
                .Select(e => (System: s.Name, Solution: e.Solutions.First(x => x.RefId == s.Solution!.Value).Name, Supplier: e.Name,
                    Contracts: string.Join(", ", e.Contracts.Where(c => !c.Customer && c.Solutions.Any(x => x.Value == s.Solution!.Value)).Select(c => c.RefId)))))
            .ToList();
        if (rows.Count == 0)
        {
            text.Append("No supplier provides them in the scope.\n");
            return;
        }

        text.Append("| System | Solution | Supplier | Contracts |\n| --- | --- | --- | --- |\n");
        foreach (var r in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {r.System} | {r.Solution} | {r.Supplier} | {(r.Contracts.Length == 0 ? "-" : r.Contracts)} |\n");
        }
    }

    private static void Contracts(StringBuilder text, ScopeDocument scope)
    {
        text.Append("\n## Customer contracts that carry the clause\n\n");
        var rows = scope.ThirdParties
            .SelectMany(e => e.Contracts.Where(c => c.Customer).Select(c => (Entity: e, Contract: c)))
            .SelectMany(x => x.Contract.Obligations.Where(o => o.Clause.Contains(Clause, StringComparison.Ordinal)).Select(o => (x.Entity, x.Contract, o.Clause)))
            .ToList();
        if (rows.Count == 0)
        {
            text.Append("No customer contract in the scope carries it: name the contract numbers on the portal from your records.\n");
            return;
        }

        text.Append("| Contract | Name | Customer | Clause |\n| --- | --- | --- | --- |\n");
        foreach (var (entity, contract, clause) in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {contract.RefId} | {contract.Name} | {entity.Name} | {clause} |\n");
        }
    }

    private static void Timeline(StringBuilder text, IncidentFacts incident)
    {
        text.Append("\n## Timeline\n\n");
        if (incident.Timeline.Count == 0)
        {
            text.Append("No timeline entries in CISO Assistant yet.\n\n");
            return;
        }

        text.Append("| When | Entry |\n| --- | --- |\n");
        foreach (var e in incident.Timeline.OrderBy(e => e.At))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Time(e.At)} | {e.Entry.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)} |\n");
        }

        text.Append('\n');
    }

    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
