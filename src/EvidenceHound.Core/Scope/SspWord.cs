using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The plan as a Word file (s11-05, <c>--format docx</c>): the same Markdown <see cref="SspLayout"/> writes, as Word headings, paragraphs,
/// lists and tables. An unsigned plan carries a DRAFT page header, so every page says so. Pure and repeatable: the same text gives the same bytes.
/// </summary>
public static partial class SspWord
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly string OfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string Main = "application/vnd.openxmlformats-officedocument.wordprocessingml";
    private static readonly string[] BorderSides = ["top", "left", "bottom", "right", "insideH", "insideV"];
    private static readonly DateTimeOffset Fixed = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Docx(string markdown, bool draft)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var parts = new List<(string Name, XDocument Xml)>
        {
            ("[Content_Types].xml", ContentTypes(draft)),
            ("_rels/.rels", Relationships(("rId1", "officeDocument", "word/document.xml"))),
            ("word/_rels/document.xml.rels", Relationships([
                ("rId1", "styles", "styles.xml"),
                ("rId2", "numbering", "numbering.xml"),
                .. draft ? [("rId3", "header", "header1.xml")] : Array.Empty<(string, string, string)>(),
            ])),
            ("word/document.xml", Document(markdown, draft)),
            ("word/styles.xml", Styles()),
            ("word/numbering.xml", Numbering()),
        };
        if (draft)
        {
            parts.Add(("word/header1.xml", new XDocument(new XElement(W + "hdr", new XAttribute(XNamespace.Xmlns + "w", W), Paragraph("Header", Run("DRAFT: not approved", bold: true))))));
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in parts)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = Fixed;
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(xml.Declaration + xml.ToString(SaveOptions.DisableFormatting));
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    private static XDocument Document(string markdown, bool draft)
    {
        var section = new XElement(
            W + "sectPr",
            draft ? new XElement(W + "headerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", "rId3")) : null,
            new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1134), new XAttribute(W + "right", 1134), new XAttribute(W + "bottom", 1134), new XAttribute(W + "left", 1134),
                new XAttribute(W + "header", 567), new XAttribute(W + "footer", 567), new XAttribute(W + "gutter", 0)));
        return Xml(new XElement(
            W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W),
            new XAttribute(XNamespace.Xmlns + "r", R),
            new XElement(W + "body", Blocks(markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')), section)));
    }

    /// <summary>Markdown lines as Word blocks: headings, quotes, bullets, tables and paragraphs (consecutive text lines join).</summary>
    private static IEnumerable<XElement> Blocks(string[] lines)
    {
        var text = new List<string>();
        var table = new List<string>();
        foreach (var line in lines.Select(l => l.TrimEnd()))
        {
            if (line.StartsWith('|'))
            {
                foreach (var block in Flush(text))
                {
                    yield return block;
                }

                table.Add(line);
                continue;
            }

            if (table.Count > 0)
            {
                yield return Table(table);
                table.Clear();
            }

            if (line.Length == 0 || Heading(line) is not null || line.StartsWith("> ", StringComparison.Ordinal) || Bullet(line) is not null)
            {
                foreach (var block in Flush(text))
                {
                    yield return block;
                }
            }

            if (Heading(line) is { } heading)
            {
                yield return heading;
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                yield return Paragraph("Quote", Inline(line[2..]));
            }
            else if (Bullet(line) is { } bullet)
            {
                yield return bullet;
            }
            else if (line.Length > 0)
            {
                text.Add(line);
            }
        }

        foreach (var block in Flush(text))
        {
            yield return block;
        }

        if (table.Count > 0)
        {
            yield return Table(table);
        }
    }

    private static IEnumerable<XElement> Flush(List<string> text)
    {
        if (text.Count == 0)
        {
            return [];
        }

        var paragraph = Paragraph(null, Inline(string.Join(' ', text)));
        text.Clear();
        return [paragraph];
    }

    private static XElement? Heading(string line)
    {
        var level = line.TakeWhile(c => c == '#').Count();
        if (level == 0 || line.Length <= level || line[level] != ' ')
        {
            return null;
        }

        var style = level switch { 1 => "Title", 2 => "Heading1", 3 => "Heading2", _ => "Heading3" };
        return Paragraph(style, Inline(line[(level + 1)..]));
    }

    private static XElement? Bullet(string line) =>
        line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) ? Paragraph("ListBullet", Inline(line[2..])) : null;

    private static XElement Table(List<string> rows)
    {
        var cells = rows.Where(r => !SeparatorRow().IsMatch(r)).Select(Cells).ToList();
        var columns = cells.Max(c => c.Count);
        return new XElement(
            W + "tbl",
            new XElement(W + "tblPr", new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")), new XElement(W + "tblW", new XAttribute(W + "w", 5000), new XAttribute(W + "type", "pct"))),
            new XElement(W + "tblGrid", Enumerable.Range(0, columns).Select(_ => new XElement(W + "gridCol"))),
            cells.Select((row, i) => new XElement(
                W + "tr",
                i == 0 ? new XElement(W + "trPr", new XElement(W + "tblHeader")) : null,
                Enumerable.Range(0, columns).Select(c => new XElement(
                    W + "tc",
                    Paragraph("TableText", Inline(c < row.Count ? row[c] : "", bold: i == 0)))))));
    }

    /// <summary>A table row's cells: split on the pipes that are not escaped, with <c>\|</c> read as a pipe.</summary>
    private static List<string> Cells(string row)
    {
        var inner = row.Trim();
        inner = inner[1..];
        if (inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal))
        {
            inner = inner[..^1];
        }

        return [.. UnescapedPipe().Split(inner).Select(c => c.Replace("\\|", "|", StringComparison.Ordinal).Trim())];
    }

    /// <summary>Inline Markdown as runs: <c>**bold**</c>, <c>_italic_</c>, <c>`code`</c> and <c>[text](link)</c>, written as text then the link.</summary>
    private static IEnumerable<XElement> Inline(string text, bool bold = false)
    {
        var at = 0;
        foreach (Match match in InlineMarkup().Matches(text))
        {
            if (match.Index > at)
            {
                yield return Run(text[at..match.Index], bold);
            }

            if (match.Groups["bold"].Success)
            {
                yield return Run(match.Groups["bold"].Value, bold: true);
            }
            else if (match.Groups["italic"].Success)
            {
                yield return Run(match.Groups["italic"].Value, bold, italic: true);
            }
            else if (match.Groups["code"].Success)
            {
                yield return Run(match.Groups["code"].Value, bold, code: true);
            }
            else
            {
                yield return Run($"{match.Groups["text"].Value} ({match.Groups["link"].Value})", bold);
            }

            at = match.Index + match.Length;
        }

        if (at < text.Length)
        {
            yield return Run(text[at..], bold);
        }
    }

    private static XElement Paragraph(string? style, params IEnumerable<XElement> runs) =>
        new(W + "p", style is null ? null : new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style))), runs);

    private static XElement Run(string text, bool bold = false, bool italic = false, bool code = false) =>
        new(
            W + "r",
            bold || italic || code
                ? new XElement(
                    W + "rPr",
                    code ? new XElement(W + "rFonts", new XAttribute(W + "ascii", "Consolas"), new XAttribute(W + "hAnsi", "Consolas")) : null,
                    bold ? new XElement(W + "b") : null,
                    italic ? new XElement(W + "i") : null)
                : null,
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    private static XDocument Styles()
    {
        static XElement Style(string id, string name, string type, int? size = null, bool bold = false, int? before = null, string? basedOn = null, int? numId = null, bool italic = false) =>
            new(
                W + "style",
                new XAttribute(W + "type", type),
                new XAttribute(W + "styleId", id),
                new XElement(W + "name", new XAttribute(W + "val", name)),
                basedOn is null ? null : new XElement(W + "basedOn", new XAttribute(W + "val", basedOn)),
                new XElement(W + "qFormat"),
                type == "paragraph"
                    ? new XElement(
                        W + "pPr",
                        new XElement(W + "spacing", new XAttribute(W + "before", before ?? 0), new XAttribute(W + "after", type == "paragraph" && id == "TableText" ? 0 : 120)),
                        numId is null ? null : new XElement(W + "numPr", new XElement(W + "numId", new XAttribute(W + "val", numId))),
                        id == "Quote" ? new XElement(W + "ind", new XAttribute(W + "left", 567)) : null)
                    : null,
                new XElement(
                    W + "rPr",
                    bold ? new XElement(W + "b") : null,
                    italic ? new XElement(W + "i") : null,
                    size is null ? null : new XElement(W + "sz", new XAttribute(W + "val", size))));

        return Xml(new XElement(
            W + "styles",
            new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "docDefaults", new XElement(W + "rPrDefault", new XElement(W + "rPr",
                new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri")),
                new XElement(W + "sz", new XAttribute(W + "val", 21))))),
            Style("Normal", "Normal", "paragraph"),
            Style("Title", "Title", "paragraph", size: 40, bold: true, basedOn: "Normal"),
            Style("Heading1", "heading 1", "paragraph", size: 30, bold: true, before: 360, basedOn: "Normal"),
            Style("Heading2", "heading 2", "paragraph", size: 26, bold: true, before: 240, basedOn: "Normal"),
            Style("Heading3", "heading 3", "paragraph", size: 22, bold: true, before: 200, basedOn: "Normal"),
            Style("ListBullet", "List Bullet", "paragraph", basedOn: "Normal", numId: 1),
            Style("Quote", "Quote", "paragraph", basedOn: "Normal", italic: true),
            Style("TableText", "Table Text", "paragraph", size: 18, basedOn: "Normal"),
            Style("Header", "header", "paragraph", basedOn: "Normal"),
            new XElement(
                W + "style",
                new XAttribute(W + "type", "table"),
                new XAttribute(W + "styleId", "TableGrid"),
                new XElement(W + "name", new XAttribute(W + "val", "Table Grid")),
                new XElement(W + "tblPr", new XElement(W + "tblBorders", BorderSides.Select(side =>
                    new XElement(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "space", 0), new XAttribute(W + "color", "auto"))))))));
    }

    private static XDocument Numbering() =>
        Xml(new XElement(
            W + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "abstractNum", new XAttribute(W + "abstractNumId", 0), new XElement(
                W + "lvl",
                new XAttribute(W + "ilvl", 0),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", "bullet")),
                new XElement(W + "lvlText", new XAttribute(W + "val", "•")),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 567), new XAttribute(W + "hanging", 283))))),
            new XElement(W + "num", new XAttribute(W + "numId", 1), new XElement(W + "abstractNumId", new XAttribute(W + "val", 0)))));

    private static XDocument ContentTypes(bool draft) =>
        Xml(new XElement(
            Types + "Types",
            new XElement(Types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            Override("/word/document.xml", $"{Main}.document.main+xml"),
            Override("/word/styles.xml", $"{Main}.styles+xml"),
            Override("/word/numbering.xml", $"{Main}.numbering+xml"),
            draft ? Override("/word/header1.xml", $"{Main}.header+xml") : null));

    private static XElement Override(string part, string type) => new(Types + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));

    private static XDocument Relationships(params (string Id, string Type, string Target)[] relationships) =>
        Xml(new XElement(
            Rels + "Relationships",
            relationships.Select(r => new XElement(Rels + "Relationship", new XAttribute("Id", r.Id), new XAttribute("Type", $"{OfficeDocument}/{r.Type}"), new XAttribute("Target", r.Target)))));

    private static XDocument Xml(XElement root) => new(new XDeclaration("1.0", "UTF-8", "yes"), root);

    [GeneratedRegex(@"^\|?\s*:?-{3,}")]
    private static partial Regex SeparatorRow();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex UnescapedPipe();

    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|(?<![\w])_(?<italic>[^_]+?)_(?![\w])|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<link>[^)]+)\)")]
    private static partial Regex InlineMarkup();
}
