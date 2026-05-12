using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Infrastructure.OpenXml;

public sealed class DocxStructureExtractor : IDocxStructureExtractor
{
    public DocumentStructure ExtractFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Extract(fs);
    }

    public DocumentStructure Extract(Stream docxStream)
    {
        using var doc = WordprocessingDocument.Open(docxStream, isEditable: false);
        var main = doc.MainDocumentPart
            ?? throw new InvalidOperationException("Documento .docx sem MainDocumentPart.");

        var styles = ExtractStyles(main);
        var headers = ExtractHeaderFooter(main.HeaderParts.Select(h => (Part: (OpenXmlPart)h, h.Header.InnerText)), "default");
        var footers = ExtractHeaderFooter(main.FooterParts.Select(f => (Part: (OpenXmlPart)f, f.Footer.InnerText)), "default");
        var sections = ExtractSections(main);
        var paragraphs = ExtractParagraphs(main);
        var tables = main.Document.Body?.Descendants<Table>().Count() ?? 0;
        var hasRevisions = HasPendingTrackChanges(main);
        var hasComments = HasOpenComments(main);

        return new DocumentStructure(
            Styles: styles,
            Headers: headers,
            Footers: footers,
            Sections: sections,
            Paragraphs: paragraphs,
            TableCount: tables,
            HasPendingTrackChanges: hasRevisions,
            HasOpenComments: hasComments);
    }

    private static IReadOnlyDictionary<string, ExtractedStyle> ExtractStyles(MainDocumentPart main)
    {
        var styles = new Dictionary<string, ExtractedStyle>(StringComparer.OrdinalIgnoreCase);
        var stylesPart = main.StyleDefinitionsPart;
        if (stylesPart?.Styles is null) return styles;

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var id = style.StyleId?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(id)) continue;
            var name = style.StyleName?.Val?.Value ?? id;

            var rPr = style.StyleRunProperties;
            var pPr = style.StyleParagraphProperties;

            string? font = rPr?.RunFonts?.Ascii?.Value;
            double? size = ParseHalfPoints(rPr?.FontSize?.Val?.Value);
            bool? bold = rPr?.Bold is { } b ? (b.Val?.Value ?? true) : (bool?)null;
            bool? italic = rPr?.Italic is { } i ? (i.Val?.Value ?? true) : (bool?)null;
            string? alignment = pPr?.Justification?.Val?.Value.ToString();

            styles[id] = new ExtractedStyle(id, name, font, size, bold, italic, alignment);
        }
        return styles;
    }

    private static double? ParseHalfPoints(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var halfPts))
            return halfPts / 2.0;
        return null;
    }

    private static IReadOnlyList<ExtractedHeaderFooter> ExtractHeaderFooter(
        IEnumerable<(OpenXmlPart Part, string InnerText)> parts,
        string kind)
    {
        return parts
            .Select(p => new ExtractedHeaderFooter(kind, (p.InnerText ?? string.Empty).Trim()))
            .Where(h => !string.IsNullOrWhiteSpace(h.Text))
            .ToList();
    }

    private static IReadOnlyList<ExtractedSection> ExtractSections(MainDocumentPart main)
    {
        var body = main.Document.Body;
        if (body is null) return Array.Empty<ExtractedSection>();
        var sections = new List<ExtractedSection>();
        int idx = 0;
        foreach (var sp in body.Descendants<SectionProperties>())
        {
            var ps = sp.GetFirstChild<PageSize>();
            var pm = sp.GetFirstChild<PageMargin>();
            sections.Add(new ExtractedSection(
                Index: idx++,
                PageWidth: ToNullableDouble(ps?.Width),
                PageHeight: ToNullableDouble(ps?.Height),
                MarginTop: ToNullableDouble(pm?.Top?.Value),
                MarginBottom: ToNullableDouble(pm?.Bottom?.Value),
                MarginLeft: ToNullableDouble(pm?.Left),
                MarginRight: ToNullableDouble(pm?.Right)));
        }
        return sections;
    }

    private static double? ToNullableDouble(object? raw)
    {
        if (raw is null) return null;
        return double.TryParse(raw.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static IReadOnlyList<ExtractedParagraph> ExtractParagraphs(MainDocumentPart main)
    {
        var body = main.Document.Body;
        if (body is null) return Array.Empty<ExtractedParagraph>();
        var list = new List<ExtractedParagraph>();
        int i = 0;
        foreach (var p in body.Descendants<Paragraph>())
        {
            var id = p.ParagraphId?.Value ?? $"p{i:0000}";
            var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            list.Add(new ExtractedParagraph(id, styleId, p.InnerText ?? string.Empty));
            i++;
        }
        return list;
    }

    private static bool HasPendingTrackChanges(MainDocumentPart main)
    {
        var body = main.Document.Body;
        if (body is null) return false;
        return body.Descendants<InsertedRun>().Any()
            || body.Descendants<DeletedRun>().Any()
            || body.Descendants<ParagraphPropertiesChange>().Any()
            || body.Descendants<RunPropertiesChange>().Any();
    }

    private static bool HasOpenComments(MainDocumentPart main)
    {
        var commentsPart = main.WordprocessingCommentsPart;
        if (commentsPart?.Comments is null) return false;
        return commentsPart.Comments.Elements<Comment>().Any();
    }
}
