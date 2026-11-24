using System.IO.Compression;
using System.Xml.Linq;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-05: <c>--format docx</c> writes the same plan as a Word file; every page of an unsigned plan says Draft.</summary>
public class SspWordTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Plan = """
        # System security plan: Example corporate IT (DRAFT)

        > **DRAFT.** Not approved: version 0.1 has no sign-off in `scope/ssp.yaml`.

        ## 1. System identification

        | Field | Value |
        | --- | --- |
        | System name | Example corporate IT |

        - Hosted by _Example Pty Ltd_.
        """;

    [Fact]
    public void Unsigned_plan_says_draft_on_every_page()
    {
        var parts = Parts(SspWord.Docx(Plan, draft: true));

        var header = parts["word/header1.xml"];
        Text(header).ShouldContain("DRAFT", Case.Sensitive);
        var sectionHeader = parts["word/document.xml"].Descendants(W + "sectPr").Single().Element(W + "headerReference")!;
        sectionHeader.Attribute(W + "type")!.Value.ShouldBe("default");
        parts["word/_rels/document.xml.rels"].ToString().ShouldContain("header1.xml");
    }

    [Fact]
    public void A_signed_plan_has_no_draft_header()
    {
        var parts = Parts(SspWord.Docx(Plan.Replace(" (DRAFT)", "", StringComparison.Ordinal), draft: false));

        parts.ShouldNotContainKey("word/header1.xml");
        parts["word/document.xml"].Descendants(W + "headerReference").ShouldBeEmpty();
    }

    [Fact]
    public void Headings_tables_and_lists_carry_over_as_word_structure()
    {
        var body = Parts(SspWord.Docx(Plan, draft: true))["word/document.xml"];

        var paragraphs = body.Descendants(W + "p").ToList();
        Style(paragraphs.First(p => Text(p) == "System security plan: Example corporate IT (DRAFT)")).ShouldBe("Title");
        Style(paragraphs.First(p => Text(p) == "1. System identification")).ShouldBe("Heading1");
        Style(paragraphs.First(p => Text(p) == "Hosted by Example Pty Ltd.")).ShouldBe("ListBullet");
        var quote = paragraphs.First(p => Text(p).StartsWith("DRAFT.", StringComparison.Ordinal));
        Text(quote).ShouldBe("DRAFT. Not approved: version 0.1 has no sign-off in scope/ssp.yaml.");
        quote.Descendants(W + "r").First().Descendants(W + "b").ShouldNotBeEmpty();
        var table = body.Descendants(W + "tbl").Single();
        table.Elements(W + "tr").Select(r => r.Elements(W + "tc").Select(Text).ToArray()).ToArray().ShouldBe([["Field", "Value"], ["System name", "Example corporate IT"]]);
    }

    [Fact]
    public void The_same_plan_gives_the_same_file()
    {
        SspWord.Docx(Plan, draft: true).ShouldBe(SspWord.Docx(Plan, draft: true));
    }

    [Fact]
    public void Text_is_escaped()
    {
        var body = Parts(SspWord.Docx("Owners & <admins> \"approve\"\n", draft: false))["word/document.xml"];

        Text(body.Descendants(W + "p").Single()).ShouldBe("Owners & <admins> \"approve\"");
    }

    private static Dictionary<string, XDocument> Parts(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        zip.GetEntry("[Content_Types].xml").ShouldNotBeNull();
        return zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal) || e.FullName.EndsWith(".rels", StringComparison.Ordinal))
            .ToDictionary(e => e.FullName, e =>
            {
                using var stream = e.Open();
                return XDocument.Load(stream);
            });
    }

    private static string Text(XContainer element) => string.Concat(element.Descendants(W + "t").Select(t => t.Value));

    private static string? Style(XElement paragraph) => paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
}
