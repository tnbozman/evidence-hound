using System.Text;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The incident response plan's layout (s15-09, maintainer's decision 2026-11-24): NIST SP 800-61 Rev 3's CSF 2.0 functions, with the
/// Australian reporting (ReportCyber, Notifiable Data Breaches, DISP) and DFARS 252.204-7012 inside them, and a section for each of
/// ISM-0043's eight items. Pure.
/// </summary>
public static class IrpLayout
{
    public const string AcscGuidance = "https://www.cyber.gov.au/sites/default/files/2023-03/ACSC%20Cyber%20Incident%20Response%20Plan%20Guidance_A4.pdf";

    /// <summary>The roles a plan names: who runs a response, who stands in, who decides for the business, who advises on law and privacy, and who approves the plan (ISM-0047).</summary>
    public static IReadOnlyList<string> Roles { get; } = ["incident_manager", "deputy", "executive", "legal_privacy", "authorising_officer"];

    /// <summary>The judgement sections a person writes; the rest of the plan is derived.</summary>
    public static IReadOnlyList<string> Sections { get; } = ["severity", "escalation", "containment", "communication", "lessons_learned"];

    /// <summary>ISM-0043's eight items (ISM September 2026, revision 6), in its order.</summary>
    public static IReadOnlyList<string> IsmItems { get; } =
    [
        "What constitutes a cyber security incident",
        "The types of incidents likely to be encountered and the expected response to each",
        "How to report incidents, internally and externally to relevant authorities",
        "Other parties to inform",
        "The authorities responsible for investigating and responding to incidents",
        "The criteria for requesting an investigation from a law enforcement agency, ASD or another authority",
        "The steps that keep evidence intact",
        "System contingency measures, or a reference to the business continuity plan",
    ];

    public static string SectionTitle(string section) => section switch
    {
        "severity" => "Severity definitions",
        "escalation" => "Escalation decisions",
        "containment" => "Containment priorities",
        "communication" => "Communication approach",
        "lessons_learned" => "Lessons learned",
        _ => section,
    };

    public static string Markdown(IrpDocument plan, IReadOnlyDictionary<string, string> sectionTexts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sectionTexts);
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');
        void Item(int number, string heading)
        {
            Line($"### {heading} (ISM-0043 item {number})");
            Line();
        }
        void Section(string name)
        {
            var file = plan.Sections.FirstOrDefault(s => s.Name == name)?.Value.Value;
            var body = file is null ? null : sectionTexts.GetValueOrDefault(file)?.Trim();
            Line($"#### {SectionTitle(name)}");
            Line();
            Line(string.IsNullOrEmpty(body) ? $"_Gap: the {SectionTitle(name).ToLowerInvariant()} are not written yet._" : body);
            Line();
        }

        Line("# Incident response plan");
        Line();
        Line($"Programme {plan.Programme}. Version {plan.Version.Value}. Built by hound from the configuration repo; each derived value names its file and line in the JSON form.");
        Line();
        if (plan.Gaps.Count > 0)
        {
            Line("## Gaps");
            Line();
            foreach (var gap in plan.Gaps)
            {
                Line($"- {gap.Requirement}: {gap.Reason}");
            }

            Line();
        }

        Line("## 1. Govern: roles, authority and approval");
        Line();
        Line("This plan sets out how the organisation prepares for, responds to and recovers from cyber security incidents. It follows NIST SP 800-61 Rev 3, with the Australian reporting from the ACSC Cyber Incident Response Plan Guidance inside it.");
        Line();
        Line("| Role | Holder |");
        Line("| --- | --- |");
        foreach (var contact in plan.Contacts)
        {
            Line($"| {contact.Role.Replace('_', ' ')} | {Cell(contact.Holder.Value)} |");
        }

        Line();
        Item(5, "Authorities responsible for investigating and responding");
        Line($"The incident manager leads the response, with the deputy standing in. The executive decides for the business. Outside the organisation: {string.Join("; ", plan.Reports.Select(r => r.ReportTo.Value).Distinct(StringComparer.Ordinal))}.");
        Line();
        Line("### Approval (ISM-0047)");
        Line();
        Line($"The plan is system-specific documentation: the authorising officer ({Holder(plan, "authorising_officer")}) approves each version.");
        Line();

        Line("## 2. Identify: systems, data and parties");
        Line();
        Line("| System | Sensitive data | Recovery objectives |");
        Line("| --- | --- | --- |");
        foreach (var system in plan.Systems)
        {
            Line($"| {Cell(system.Name.Value)} | {Cell(string.Join("; ", system.Data.Select(d => d.Value)))} | {Cell(system.Recovery.Value)} |");
        }

        Line();
        Item(4, "Other parties to inform");
        if (plan.Parties.Count == 0)
        {
            Line("No supplier or customer is declared in scope/third-parties/ yet.");
        }
        else
        {
            Line("| Party | Kind | Contacts | Contract clauses |");
            Line("| --- | --- | --- | --- |");
            foreach (var party in plan.Parties)
            {
                Line($"| {Cell(party.Name.Value)} | {party.Kind} | {Cell(string.Join(", ", party.Contacts.Select(c => c.Value)))} | {Cell(string.Join(", ", party.Clauses.Select(c => c.Value)))} |");
            }
        }

        Line();
        Section("communication");

        Line("## 3. Protect: preparation");
        Line();
        Item(7, "Steps that keep evidence intact");
        Line("Before anything is changed: note the time and who saw what; take memory and disk images or export the logs where the tools allow; keep the original media and logs read-only; record each item, who holds it and when it passed hands; and do not wipe, rebuild or power off a system the incident manager has not released.");
        Line();

        Line("## 4. Detect: what an incident is");
        Line();
        Item(1, "What constitutes a cyber security incident");
        Line("A cyber security incident is an event that harms, or may harm, the confidentiality, integrity or availability of a system or its data, as set by the severity definitions below.");
        Line();
        Section("severity");
        Item(2, "Incident types and expected responses");
        Section("containment");

        Line("## 5. Respond: escalate, report and investigate");
        Line();
        Section("escalation");
        Item(3, "Reporting internally and externally");
        Line("Internally, anyone who sees a possible incident tells the incident manager at once. Externally, each report below is due within its deadline from discovery: record each on the incident's timeline in CISO Assistant, and `hound incident obligations` lists what is still owed.");
        Line();
        Line("| Report | To | Within | When | Reference |");
        Line("| --- | --- | --- | --- | --- |");
        foreach (var report in plan.Reports)
        {
            Line($"| {Cell(report.Name.Value)} | {Cell(report.ReportTo.Value)} | {Cell(report.Within.Value)} | {Cell(report.Trigger.Value)} | {Cell(report.Reference.Value)} |");
        }

        Line();
        Item(6, "Criteria for requesting an investigation");
        Line("The executive, advised by the legal and privacy contact, asks the police (through ReportCyber) when a crime is suspected, such as extortion, fraud or theft of data; asks ASD's ACSC for help when the incident is beyond the team or affects a critical system; and tells Defence or a prime when their information or systems may be involved.");
        Line();

        Line("## 6. Recover: restore and learn");
        Line();
        Item(8, "System contingency measures");
        Line("Restore systems in the order and within the recovery objectives of the business impact analysis (scope/bia/), listed under Identify above; the business continuity summary holds the wider measures.");
        Line();
        Section("lessons_learned");

        Line("## References");
        Line();
        Line("- ISM-0043 (incident response plan) and ISM-0047 (approval of system-specific documentation), Information Security Manual.");
        Line("- NIST SP 800-61 Rev 3, Incident Response Recommendations and Considerations for Cybersecurity Risk Management (2025).");
        Line($"- ACSC Cyber Incident Response Plan Guidance: {AcscGuidance}");
        Line("- NIST SP 800-171 3.6.1 to 3.6.3: incident handling, reporting and testing.");
        Line($"- The plan is tested each year (manual check `incident.response_plan_tested`); the test records the version it tested, here {plan.Version.Value}.");
        return text.ToString();
    }

    private static string Holder(IrpDocument plan, string role) => plan.Contacts.FirstOrDefault(c => c.Role == role)?.Holder.Value ?? "not named yet";

    private static string Cell(string? text) => string.IsNullOrEmpty(text) ? "" : text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
