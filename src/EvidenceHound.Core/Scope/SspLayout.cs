using System.Globalization;
using System.Text;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The plan as Markdown in the one layout hound supports (s11-04): NIST SP 800-18 Rev 1 Appendix A, sections 1 to 15, with the controls annex
/// as section 13 and the POA&amp;M as an appendix. Only the annex depends on the framework. Pure.
/// </summary>
public static class SspLayout
{
    public static string Markdown(SspDocument document, IReadOnlyDictionary<string, string> sections)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sections);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# System security plan: {Cell(document.SystemName)}\n\n")
            .Append(CultureInfo.InvariantCulture, $"Programme `{document.Programme}`, profile `{document.Profile}`, version {Cell(document.Version)}.\n\n")
            .Append("This plan follows NIST SP 800-18 Rev 1 Appendix A. The overview (sections 1 to 12) and the annex (section 13) meet ISM-0041: ")
            .Append("the system's purpose, its boundary, how it is managed, and the applicable controls.\n\n");

        Gaps(text, document);

        Heading(text, "1. System name and identifier")
            .Append(CultureInfo.InvariantCulture, $"- Name: {Cell(document.SystemName)}\n")
            .Append(CultureInfo.InvariantCulture, $"- Boundary (perimeter): {Cell(document.Perimeter)}\n")
            .Append(CultureInfo.InvariantCulture, $"- Audit: {Cell(document.Audit)}\n\n");
        Heading(text, "2. System categorisation").Append(CultureInfo.InvariantCulture, $"- Classification: {Cell(document.Classification)}\n\n");
        Heading(text, "3. System owner").Append(CultureInfo.InvariantCulture, $"{Role(document, "system_owner")}\n\n");
        Heading(text, "4. Authorising official").Append(CultureInfo.InvariantCulture, $"{Role(document, "authorising_official")}\n\n");
        Heading(text, "5. Other designated contacts").Append(CultureInfo.InvariantCulture, $"- Security contact: {Role(document, "security_contact")}\n\n");
        Heading(text, "6. Assignment of security responsibility").Append(Section(sections, "management"));
        Heading(text, "7. System operational status").Append(CultureInfo.InvariantCulture, $"{Cell(document.OperationalStatus)}\n\n");
        Heading(text, "8. System type").Append(CultureInfo.InvariantCulture, $"{Cell(document.SystemType)}\n\n");
        Heading(text, "9. General description and purpose").Append(Section(sections, "purpose"));
        Heading(text, "10. System environment").Append(Section(sections, "environment"));
        Inventory(text, document);
        Heading(text, "11. Interconnections and external service providers");
        Services(text, document);
        Heading(text, "12. Applicable laws, regulations and policies").Append(Section(sections, "laws_and_policies"));
        foreach (var d in document.Documents)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {Cell(d)}\n");
        }

        text.Append(document.Documents.Count == 0 ? "" : "\n");
        Heading(text, "13. Security controls (annex)");
        Annex(text, document);
        Heading(text, "14. Completion and approval dates")
            .Append(CultureInfo.InvariantCulture, $"Version {Cell(document.Version)}. The authorising official ({Role(document, "authorising_official")}) approves the plan in CISO Assistant; the approval date is recorded there.\n\n");
        Heading(text, "15. Ongoing system security plan maintenance")
            .Append("`hound ssp build` rebuilds this plan from CISO Assistant and the configuration repo. Review it at least once a year and after any significant change to the system.\n\n");
        Heading(text, "Appendix: plan of action and milestones")
            .Append(Poam.Markdown(Poam.Of(document)).Replace("## Plan of action and milestones\n\n", "", StringComparison.Ordinal));
        return text.ToString();
    }

    private static StringBuilder Heading(StringBuilder text, string title) => text.Append(CultureInfo.InvariantCulture, $"## {title}\n\n");

    private static void Gaps(StringBuilder text, SspDocument document)
    {
        Heading(text, "Gaps");
        if (document.Gaps.Count == 0)
        {
            text.Append("No gaps.\n\n");
            return;
        }

        foreach (var g in document.Gaps)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {g.Requirement}: {g.Reason}\n");
        }

        text.Append('\n');
    }

    private static void Inventory(StringBuilder text, SspDocument document)
    {
        text.Append("| System | Type | Owner | Description |\n| --- | --- | --- | --- |\n");
        foreach (var s in document.Inventory)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Cell(s.Name)} | {Cell(s.Type)} | {Cell(s.Owner)} | {Cell(s.Description)} |\n");
        }

        text.Append('\n');
    }

    private static void Services(StringBuilder text, SspDocument document)
    {
        if (document.ExternalServices.Count == 0)
        {
            text.Append("No external services.\n\n");
            return;
        }

        text.Append("| System | Solution | Supplier | Criticality |\n| --- | --- | --- | --- |\n");
        foreach (var s in document.ExternalServices)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Cell(s.System)} | {Cell(s.Solution)} | {Cell(s.Supplier)} | {Cell(s.Criticality)} |\n");
        }

        text.Append('\n');
    }

    private static void Annex(StringBuilder text, SspDocument document)
    {
        if (document.Annex.Count == 0)
        {
            text.Append("No annex: the framework snapshot was not available.\n\n");
            return;
        }

        text.Append("Columns follow the ASD SSP annex template. Implementation status uses the ASD SSP annex template's terms (Effective, Ineffective, ")
            .Append("Not implemented, Not applicable, Not assessed); the SP 800-18 status beside it decides what the plan of action and milestones carries. ")
            .Append("Control type comes from the framework's reference controls.\n\n")
            .Append("| Control | Description | Strategies | Maturity levels | Control type | Responsible entity | Implementation status | SP 800-18 status | Statement | Responsible | POA&M |\n")
            .Append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (var a in document.Annex)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {a.Control} | {Escape(a.Description.Length > 0 ? a.Description : a.Title)} | {string.Join(", ", a.Parents)} | {List(a.MaturityLevels)} | {List(a.ControlTypes)} | ")
                .Append(CultureInfo.InvariantCulture, $"{Cell(a.ResponsibleEntity)} | {Cell(a.AsdStatus)} | {Cell(a.Status)} | {Cell(a.Statement)} | {Cell(a.Responsible)} | {a.Poam ?? "-"} |\n");
        }

        text.Append('\n');
    }

    private static string List(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(", ", values);

    private static string Role(SspDocument document, string role) =>
        document.Roles.FirstOrDefault(r => r.Role == role) is { } held ? Cell(held.Holder) : "-";

    private static string Section(IReadOnlyDictionary<string, string> sections, string name) =>
        sections.TryGetValue(name, out var body) && !string.IsNullOrWhiteSpace(body) ? $"{body.Trim()}\n\n" : $"_Section `{name}` is not written yet._\n\n";

    private static string Cell(SspValue value) => value.Value is null ? "-" : Escape(value.Value);

    private static string Escape(string value) => value.Trim().Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
