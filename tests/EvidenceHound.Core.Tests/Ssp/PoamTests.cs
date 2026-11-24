using EvidenceHound.Core.Scope;
using static EvidenceHound.Core.Tests.Ssp.SspStatementTests;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-03: the POA&amp;M lists every requirement not yet met, with its control, owner, ETA and status from CISO Assistant.</summary>
public class PoamTests
{
    [Fact]
    public void Lists_unmet_requirements_with_owner_and_eta()
    {
        var controls = Controls(Control("AC-A", "Does A.", "it@example.com", "2026-12-31", "in_progress"), Control("AC-B", "Does B.", "ceo@example.com"));
        var document = Document(
            SspStatements.Complete(Row("ISM-3", "compliant", "Fine.", "AC-B"), controls),
            SspStatements.Complete(Row("ISM-2", "non_compliant", "Not yet.", "AC-A"), controls),
            SspStatements.Complete(Row("ISM-1", "partially_compliant", "Half.", "AC-B", "AC-A"), controls),
            SspStatements.Complete(Row("ISM-4", "not_assessed", null), controls),
            SspStatements.Complete(Row("ISM-5", "not_applicable", "No Java."), controls));

        var items = Poam.Of(document);

        items.Select(i => (i.Requirement.Value, i.Status.Value, i.Control.Value, i.Owner.Value, i.Eta.Value, i.ControlStatus.Value)).ShouldBe(
        [
            ("ISM-1", "Partially implemented", "AC-A", "it@example.com", "2026-12-31", "in_progress"),
            ("ISM-1", "Partially implemented", "AC-B", "ceo@example.com", null, "in_progress"),
            ("ISM-2", "Planned", "AC-A", "it@example.com", "2026-12-31", "in_progress"),
            ("ISM-4", "Not implemented", null, null, null, null),
        ]);
        items[0].Eta.Source.ShouldBe(new SspSource(SspSource.CisoAssistant, "applied control AC-A"));
    }

    [Fact]
    public void Markdown_is_a_table_with_one_line_per_item()
    {
        var controls = Controls(Control("AC-A", "Does A.", "it@example.com", "2026-12-31"));
        var markdown = Poam.Markdown(Poam.Of(Document(SspStatements.Complete(Row("ISM-2", "non_compliant", "Not yet.", "AC-A"), controls))));

        markdown.ShouldContain("| Requirement | Status | Control | Owner | ETA | Control status |");
        markdown.ShouldContain("| ISM-2 | Planned | AC-A | it@example.com | 2026-12-31 | in_progress |");
    }
}
