using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Abstractions;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.OpenXml;

public sealed class DocxStructureExtractor : IDocxStructureExtractor
{
    public DocumentStructure ExtractFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Extract(fs, Path.GetFileName(path));
    }

    public DocumentStructure Extract(Stream docxStream, string? fileName = null)
    {
        using var doc = WordprocessingDocument.Open(docxStream, isEditable: false);
        var main = doc.MainDocumentPart
            ?? throw new InvalidOperationException("Documento .docx sem MainDocumentPart.");

        var styles = ExtractStyles(main);
        // Mapeia HeaderReference/FooterReference -> Type (default/first/even) por relId.
        var headerKinds = ResolveHeaderFooterKinds<HeaderReference>(main);
        var footerKinds = ResolveHeaderFooterKinds<FooterReference>(main);
        var headers = ExtractHeaderFooter(
            main.HeaderParts.Select(h => (
                Element: (DocumentFormat.OpenXml.OpenXmlPartRootElement)h.Header,
                Text: h.Header.InnerText,
                Kind: headerKinds.GetValueOrDefault(main.GetIdOfPart(h), "default"))));
        var footers = ExtractHeaderFooter(
            main.FooterParts.Select(f => (
                Element: (DocumentFormat.OpenXml.OpenXmlPartRootElement)f.Footer,
                Text: f.Footer.InnerText,
                Kind: footerKinds.GetValueOrDefault(main.GetIdOfPart(f), "default"))));
        var sections = ExtractSections(main);
        var paragraphs = ExtractParagraphs(main);
        var tables = ExtractTables(main);
        var hasRevisions = HasPendingTrackChanges(main);
        var hasComments = HasOpenComments(main);
        var tocUpdated = HasUpdatedToc(main);
        var bodyFields = main.Document.Body is null
            ? Array.Empty<string>()
            : ExtractFieldCodes(main.Document.Body);

        return new DocumentStructure(
            FileName: fileName,
            Styles: styles,
            Headers: headers,
            Footers: footers,
            Sections: sections,
            Paragraphs: paragraphs,
            Tables: tables,
            HasPendingTrackChanges: hasRevisions,
            HasOpenComments: hasComments,
            HasUpdatedToc: tocUpdated,
            BodyFieldCodes: bodyFields);
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
        IEnumerable<(DocumentFormat.OpenXml.OpenXmlPartRootElement Element, string Text, string Kind)> parts)
    {
        var list = new List<ExtractedHeaderFooter>();
        foreach (var (element, text, kind) in parts)
        {
            var images = element.Descendants<Drawing>().Count()
                       + element.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().Count();
            var fieldCodes = ExtractFieldCodes(element);
            list.Add(new ExtractedHeaderFooter(kind, (text ?? string.Empty).Trim(), images, fieldCodes));
        }
        return list;
    }

    /// <summary>
    /// Mapeia o relId de cada HeaderReference/FooterReference para o seu tipo
    /// (default/first/even) percorrendo as SectionProperties do documento.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ResolveHeaderFooterKinds<TRef>(MainDocumentPart main)
        where TRef : OpenXmlElement
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = main.Document.Body;
        if (body is null) return map;
        foreach (var sectPr in body.Descendants<SectionProperties>())
        {
            foreach (var refEl in sectPr.Elements<TRef>())
            {
                string? relId = null;
                string kind = "default";
                if (refEl is HeaderReference hr)
                {
                    relId = hr.Id?.Value;
                    kind = ResolveKind(hr.Type?.Value.ToString());
                }
                else if (refEl is FooterReference fr)
                {
                    relId = fr.Id?.Value;
                    kind = ResolveKind(fr.Type?.Value.ToString());
                }
                if (!string.IsNullOrEmpty(relId) && !map.ContainsKey(relId))
                    map[relId] = kind;
            }
        }
        return map;
    }

    /// <summary>
    /// Coleta nomes de campos OOXML (PAGE, NUMPAGES, TOC, REF etc.) presentes em
    /// um SectionPartRootElement (header/footer ou body). Captura tanto SimpleField
    /// (<c>w:fldSimple w:instr="PAGE"</c>) quanto a forma estendida com FieldCode.
    /// </summary>
    private static IReadOnlyList<string> ExtractFieldCodes(DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var codes = new List<string>();

        foreach (var simple in element.Descendants<SimpleField>())
        {
            var instr = simple.Instruction?.Value;
            var name = ExtractFieldName(instr);
            if (!string.IsNullOrEmpty(name)) codes.Add(name);
        }

        foreach (var fc in element.Descendants<FieldCode>())
        {
            var name = ExtractFieldName(fc.Text);
            if (!string.IsNullOrEmpty(name)) codes.Add(name);
        }

        return codes;
    }

    private static string? ExtractFieldName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.TrimStart();
        // Field instructions começam com o nome do campo (ex.: "PAGE \\* MERGEFORMAT").
        var space = trimmed.IndexOfAny(new[] { ' ', '\t' });
        var name = space < 0 ? trimmed : trimmed[..space];
        return name.Trim().ToUpperInvariant();
    }

    private static string ResolveKind(string? rawType) => (rawType ?? string.Empty).ToLowerInvariant() switch
    {
        "first" => "first",
        "even" => "even",
        _ => "default"
    };

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
            // Page break: <w:br w:type="page"/> dentro do parágrafo (run-level)
            // ou no ParagraphProperties (page break before).
            var hasPageBreak = p.Descendants<Break>().Any(b =>
                                    b.Type?.Value == BreakValues.Page)
                            || (p.ParagraphProperties?.PageBreakBefore is not null);
            list.Add(new ExtractedParagraph(id, styleId, p.InnerText ?? string.Empty, hasPageBreak));
            i++;
        }
        return list;
    }

    private static IReadOnlyList<ExtractedTable> ExtractTables(MainDocumentPart main)
    {
        var body = main.Document.Body;
        if (body is null) return Array.Empty<ExtractedTable>();
        var list = new List<ExtractedTable>();
        int idx = 0;
        foreach (var t in body.Descendants<Table>())
        {
            var cells = new List<ExtractedTableCell>();
            int rowIdx = 0;
            string? firstRowText = null;
            foreach (var row in t.Elements<TableRow>())
            {
                int colIdx = 0;
                var rowTexts = new List<string>();
                foreach (var cell in row.Elements<TableCell>())
                {
                    var text = (cell.InnerText ?? string.Empty).Trim();
                    cells.Add(new ExtractedTableCell(rowIdx, colIdx, text));
                    rowTexts.Add(text);
                    colIdx++;
                }
                if (rowIdx == 0) firstRowText = string.Join(" | ", rowTexts);
                rowIdx++;
            }
            list.Add(new ExtractedTable(idx++, cells, firstRowText));
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

    private static bool HasUpdatedToc(MainDocumentPart main)
    {
        // TOC heuristic: SDT block containing a "TOC" docPartGallery or paragraphs with "tocXX" style.
        var body = main.Document.Body;
        if (body is null) return false;
        if (body.Descendants<SdtBlock>().Any(s => s.InnerText.Contains("Sumário", StringComparison.OrdinalIgnoreCase)
                                              || s.InnerText.Contains("Índice", StringComparison.OrdinalIgnoreCase)))
            return true;
        return body.Descendants<Paragraph>()
            .Any(p => (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "")
                .StartsWith("toc", StringComparison.OrdinalIgnoreCase));
    }
}
