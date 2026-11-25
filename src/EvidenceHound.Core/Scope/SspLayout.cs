using System.Globalization;
using System.Text;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The plan as Markdown in the one layout hound supports (s11-04): NIST SP 800-18 Rev 1 Appendix A, sections 1 to 15, with the controls annex
/// as section 13 and the POA&amp;M as an appendix. Only the annex depends on the framework. Pure.
/// </summary>
public static class SspLayout
{
    /// <summary>The plan; a draft, marked as such, unless <paramref name="signOff"/> is the authorising official's sign-off of this draft (s11-05).</summary>
    public static string Markdown(SspDocument document, IReadOnlyDictionary<string, string> sections, SspSignOff? signOff = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sections);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# System security plan: {Cell(document.SystemName)}{(signOff is null ? " (DRAFT)" : "")}\n\n")
            .Append(signOff is null
                ? $"> **DRAFT.** Not approved: version {Cell(document.Version)} has no sign-off in `scope/ssp.yaml`.\n\n"
                : $"> Approved by {signOff.By} on {signOff.Date}. SHA-256 of the reviewed draft: {signOff.Sha256}.\n\n")
            .Append(CultureInfo.InvariantCulture, $"Programme `{document.Programme}`, profile `{document.Profile}`, version {Cell(document.Version)}.\n\n")
            .Append(Nist(document)
                ? "This plan follows NIST SP 800-18 Rev 1 Appendix A. It is the system security plan NIST SP 800-171 requirement 3.12.4 calls for: "
                  + "the system boundary, its environment, how each requirement is implemented, and its connections to other systems.\n\n"
                : "This plan follows NIST SP 800-18 Rev 1 Appendix A. The overview (sections 1 to 12) and the annex (section 13) meet ISM-0041: "
                  + "the system's purpose, its boundary, how it is managed, and the applicable controls.\n\n");

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
            .Append(signOff is null
                ? $"Version {Cell(document.Version)} is a draft. The authorising official ({Role(document, "authorising_official")}) approves it by recording a sign-off in `scope/ssp.yaml`, through a reviewed pull request.\n\n"
                : $"Version {Cell(document.Version)} was approved by the authorising official, {signOff.By}, on {signOff.Date}.\n\n");
        Heading(text, "15. Ongoing system security plan maintenance")
            .Append("`hound ssp build` rebuilds this plan from CISO Assistant and the configuration repo. Review it at least once a year and after any significant change to the system.\n\n");
        Heading(text, "Appendix: plan of action and milestones")
            .Append(Poam.Markdown(Poam.Of(document)).Replace("## Plan of action and milestones\n\n", "", StringComparison.Ordinal))
            .Append(Nist(document)
                ? "\nA Conditional CMMC Level 2 status also needs an SPRS score of at least 88 (0.8 of 110) and every item closed within 180 days "
                  + $"({CmmcPoam.Source}(i) and 170.21(b)). `hound sprs score` works out the score.\n"
                : "");
        return text.Append(signOff is null ? "\n_DRAFT: not approved._\n" : "").ToString();
    }

    private static bool Nist(SspDocument document) => document.Profile == SspProfile.Nist800171;

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
        var nist = Nist(document);
        text.Append(nist ? "| System | Type | Owner | Description | CMMC asset category |\n| --- | --- | --- | --- | --- |\n" : "| System | Type | Owner | Description |\n| --- | --- | --- | --- |\n");
        foreach (var s in document.Inventory)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {Cell(s.Name)} | {Cell(s.Type)} | {Cell(s.Owner)} | {Cell(s.Description)} |")
                .Append(nist ? $" {(s.Category.Value is { } c ? CmmcAssetCategory.Name(c) ?? Escape(c) : "-")} |\n" : "\n");
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

        if (Nist(document))
        {
            FamilyAnnex(text, document);
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

    /// <summary>The <c>nist-800-171</c> annex (s17-03): every requirement under its family, with status, statement, responsible party, evidence and POA&amp;M item.</summary>
    private static void FamilyAnnex(StringBuilder text, SspDocument document)
    {
        text.Append("Every requirement CMMC Level 2 assesses (NIST SP 800-171 Rev 2), grouped by family. Status uses the SP 800-18 terms; a requirement ")
            .Append("with no row in the audit is not assessed and shown as Not implemented. A POA&M item marked \"not allowed\" cannot stay on a CMMC POA&M ")
            .Append(CultureInfo.InvariantCulture, $"({CmmcPoam.Source}): see the gaps.\n\n");
        foreach (var family in document.Annex.GroupBy(a => a.Family, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"### {family.Key}\n\n")
                .Append("| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |\n")
                .Append("| --- | --- | --- | --- | --- | --- | --- | --- |\n");
            foreach (var a in family)
            {
                var poam = a.Poam is null ? "-" : document.PoamNotAllowed.ContainsKey(a.Control) ? $"{a.Poam} (not allowed)" : a.Poam;
                text.Append(CultureInfo.InvariantCulture, $"| {a.Control} | {Escape(a.Title)} | {List(a.MaturityLevels)} | {Cell(a.Status)} | {Cell(a.Statement)} | ")
                    .Append(CultureInfo.InvariantCulture, $"{Cell(a.Responsible)} | {Cell(a.Evidence)} | {poam} |\n");
            }

            text.Append('\n');
        }
    }

    private static string List(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(", ", values);

    private static string Role(SspDocument document, string role) =>
        document.Roles.FirstOrDefault(r => r.Role == role) is { } held ? Cell(held.Holder) : "-";

    private static string Section(IReadOnlyDictionary<string, string> sections, string name) =>
        sections.TryGetValue(name, out var body) && !string.IsNullOrWhiteSpace(body) ? $"{body.Trim()}\n\n" : $"_Section `{name}` is not written yet._\n\n";

    private static string Cell(SspValue value) => value.Value is null ? "-" : Escape(value.Value);

    private static string Escape(string value) => value.Trim().Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
