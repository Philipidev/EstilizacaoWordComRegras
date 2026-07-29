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
        var resolver = new StyleResolver(main);
        // Mapeia HeaderReference/FooterReference -> (tipo, seção) por relId.
        var headerKinds = ResolveHeaderFooterKinds<HeaderReference>(main);
        var footerKinds = ResolveHeaderFooterKinds<FooterReference>(main);
        var headers = ExtractHeaderFooter(
            main.HeaderParts.Select(h => (
                Element: (DocumentFormat.OpenXml.OpenXmlPartRootElement)h.Header,
                Text: h.Header.InnerText,
                Binding: headerKinds.GetValueOrDefault(main.GetIdOfPart(h), new HeaderFooterBinding("default", null)))));
        var footers = ExtractHeaderFooter(
            main.FooterParts.Select(f => (
                Element: (DocumentFormat.OpenXml.OpenXmlPartRootElement)f.Footer,
                Text: f.Footer.InnerText,
                Binding: footerKinds.GetValueOrDefault(main.GetIdOfPart(f), new HeaderFooterBinding("default", null)))));
        var sections = ExtractSections(main);
        var paragraphs = ExtractParagraphs(main, resolver);
        var tables = ExtractTables(main);
        var hasRevisions = HasPendingTrackChanges(main);
        var hasComments = HasOpenComments(main);
        var tocUpdated = HasUpdatedToc(main);
        var bodyFields = main.Document.Body is null
            ? Array.Empty<string>()
            : ExtractFieldCodes(main.Document.Body);
        var properties = ExtractDocumentProperties(doc);

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
            BodyFieldCodes: bodyFields,
            Properties: properties);
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
            string? alignment = EnumText(pPr?.Justification?.Val);

            styles[id] = new ExtractedStyle(id, name, font, size, bold, italic, alignment);
        }
        return styles;
    }

    /// <summary>
    /// Valor bruto de um atributo enumerado do OOXML ("center", "right", "first", "even"…).
    /// A partir do SDK 3.x esses tipos são structs e <c>ToString()</c> devolve o nome do tipo
    /// ("JustificationValues { }"), não o valor — por isso a leitura é sempre via InnerText.
    /// </summary>
    private static string? EnumText(OpenXmlSimpleType? value)
    {
        var raw = value?.InnerText;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
    }

    private static double? ParseHalfPoints(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var halfPts))
            return halfPts / 2.0;
        return null;
    }

    /// <summary>
    /// Formatação efetiva de um estilo, já resolvida pela cadeia <c>w:basedOn</c> e pelos
    /// <c>docDefaults</c>. É o que permite comparar "Times New Roman 12" com o que o
    /// documento realmente aplica, e não apenas com o que a definição local do estilo diz.
    /// </summary>
    private sealed record StyleFormat(string? Font, double? Size, string? Alignment, int? OutlineLevel);

    private sealed class StyleResolver
    {
        private readonly Dictionary<string, Style> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StyleFormat> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly StyleFormat _defaults;

        public StyleResolver(MainDocumentPart main)
        {
            var stylesPart = main.StyleDefinitionsPart;
            if (stylesPart?.Styles is not null)
            {
                foreach (var s in stylesPart.Styles.Elements<Style>())
                {
                    var id = s.StyleId?.Value;
                    if (!string.IsNullOrEmpty(id)) _byId[id] = s;
                }
            }

            var docDefaults = stylesPart?.Styles?.DocDefaults;
            _defaults = new StyleFormat(
                Font: docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.RunFonts?.Ascii?.Value,
                Size: ParseHalfPoints(docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.FontSize?.Val?.Value),
                Alignment: EnumText(docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle?.Justification?.Val),
                OutlineLevel: null);
        }

        public StyleFormat Defaults => _defaults;

        public StyleFormat Resolve(string? styleId)
        {
            if (string.IsNullOrEmpty(styleId)) return _defaults;
            if (_cache.TryGetValue(styleId, out var cached)) return cached;

            // Guarda contra basedOn cíclico (documentos gerados por ferramentas de terceiros).
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<Style>();
            var current = styleId;
            while (!string.IsNullOrEmpty(current) && visited.Add(current) && _byId.TryGetValue(current, out var style))
            {
                chain.Add(style);
                current = style.BasedOn?.Val?.Value;
            }

            // Do ancestral mais distante para o mais específico: o último a definir vence.
            string? font = _defaults.Font;
            double? size = _defaults.Size;
            string? alignment = _defaults.Alignment;
            int? outline = null;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var s = chain[i];
                font = s.StyleRunProperties?.RunFonts?.Ascii?.Value ?? font;
                size = ParseHalfPoints(s.StyleRunProperties?.FontSize?.Val?.Value) ?? size;
                alignment = EnumText(s.StyleParagraphProperties?.Justification?.Val) ?? alignment;
                outline = s.StyleParagraphProperties?.OutlineLevel?.Val?.Value ?? outline;
            }

            outline ??= InferOutlineFromName(styleId, chain.Count > 0 ? chain[0].StyleName?.Val?.Value : null);

            var result = new StyleFormat(font, size, alignment, outline);
            _cache[styleId] = result;
            return result;
        }

        /// <summary>
        /// Estilos de título nem sempre declaram <c>w:outlineLvl</c>. O nome canônico em
        /// styles.xml é sempre inglês ("heading 1"), mas o styleId pode vir localizado
        /// ("Ttulo1") em documentos criados no Word em PT-BR.
        /// </summary>
        private static int? InferOutlineFromName(string? styleId, string? styleName)
        {
            foreach (var candidate in new[] { styleName, styleId })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var normalized = candidate.Trim().ToLowerInvariant().Replace(" ", "");
                foreach (var prefix in new[] { "heading", "ttulo", "titulo", "título" })
                {
                    if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var rest = normalized[prefix.Length..];
                    if (int.TryParse(rest, out var level) && level is >= 1 and <= 9)
                        return level - 1; // outlineLvl é 0-based: "heading 1" -> 0
                }
            }
            return null;
        }
    }

    private sealed record HeaderFooterBinding(string Kind, int? SectionIndex);

    private static IReadOnlyList<ExtractedHeaderFooter> ExtractHeaderFooter(
        IEnumerable<(DocumentFormat.OpenXml.OpenXmlPartRootElement Element, string Text, HeaderFooterBinding Binding)> parts)
    {
        var list = new List<ExtractedHeaderFooter>();
        foreach (var (element, text, binding) in parts)
        {
            var images = element.Descendants<Drawing>().Count()
                       + element.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().Count();
            var fieldCodes = ExtractFieldCodes(element);
            var alignments = element.Descendants<Paragraph>()
                .Select(p => EnumText(p.ParagraphProperties?.Justification?.Val) ?? "left")
                .ToList();
            list.Add(new ExtractedHeaderFooter(
                binding.Kind, (text ?? string.Empty).Trim(), images, fieldCodes,
                alignments, binding.SectionIndex));
        }
        return list;
    }

    /// <summary>
    /// Mapeia o relId de cada HeaderReference/FooterReference para o seu tipo
    /// (default/first/even) e o índice da seção que o referencia, percorrendo as
    /// SectionProperties do documento na ordem em que aparecem.
    /// </summary>
    private static IReadOnlyDictionary<string, HeaderFooterBinding> ResolveHeaderFooterKinds<TRef>(MainDocumentPart main)
        where TRef : OpenXmlElement
    {
        var map = new Dictionary<string, HeaderFooterBinding>(StringComparer.Ordinal);
        var body = main.Document.Body;
        if (body is null) return map;
        var sectionIndex = 0;
        foreach (var sectPr in body.Descendants<SectionProperties>())
        {
            foreach (var refEl in sectPr.Elements<TRef>())
            {
                string? relId = null;
                string kind = "default";
                if (refEl is HeaderReference hr)
                {
                    relId = hr.Id?.Value;
                    kind = ResolveKind(EnumText(hr.Type));
                }
                else if (refEl is FooterReference fr)
                {
                    relId = fr.Id?.Value;
                    kind = ResolveKind(EnumText(fr.Type));
                }
                if (!string.IsNullOrEmpty(relId) && !map.ContainsKey(relId))
                    map[relId] = new HeaderFooterBinding(kind, sectionIndex);
            }
            sectionIndex++;
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

    private static IReadOnlyList<ExtractedParagraph> ExtractParagraphs(MainDocumentPart main, StyleResolver resolver)
    {
        var body = main.Document.Body;
        if (body is null) return Array.Empty<ExtractedParagraph>();
        var list = new List<ExtractedParagraph>();
        int i = 0;
        int sectionIndex = 0;
        foreach (var p in body.Descendants<Paragraph>())
        {
            var id = p.ParagraphId?.Value ?? $"p{i:0000}";
            var pPr = p.ParagraphProperties;
            var styleId = pPr?.ParagraphStyleId?.Val?.Value;
            var styleFmt = resolver.Resolve(styleId);

            // Page break: <w:br w:type="page"/> dentro do parágrafo (run-level)
            // ou no ParagraphProperties (page break before).
            var hasPageBreak = p.Descendants<Break>().Any(b =>
                                    b.Type?.Value == BreakValues.Page)
                            || (pPr?.PageBreakBefore is not null);

            var alignment = EnumText(pPr?.Justification?.Val) ?? styleFmt.Alignment;
            var outline = pPr?.OutlineLevel?.Val?.Value ?? styleFmt.OutlineLevel;
            var numId = pPr?.NumberingProperties?.NumberingId?.Val?.Value;
            var (font, size) = ResolveEffectiveRunFormat(p, styleFmt);
            var images = p.Descendants<Drawing>().Count()
                       + p.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().Count();
            var inTable = p.Ancestors<TableCell>().Any();

            list.Add(new ExtractedParagraph(
                ParagraphId: id,
                StyleId: styleId,
                Text: p.InnerText ?? string.Empty,
                EndsWithPageBreak: hasPageBreak,
                Alignment: alignment,
                OutlineLevel: outline,
                NumberingId: numId,
                EffectiveFont: font,
                EffectiveFontSize: size,
                ImageCount: images,
                SectionIndex: sectionIndex,
                IsInTable: inTable,
                FieldCodes: ExtractFieldCodes(p)));

            // Um sectPr dentro do pPr encerra a seção — o próximo parágrafo já é da seguinte.
            if (pPr?.SectionProperties is not null) sectionIndex++;
            i++;
        }
        return list;
    }

    /// <summary>
    /// Fonte e tamanho predominantes do parágrafo, ponderados pelo comprimento do texto de
    /// cada run. A formatação direta do run tem precedência sobre o estilo — sem isso um
    /// documento com estilo "Normal/Arial" mas runs marcados Times New Roman seria julgado
    /// pelo estilo, e não pelo que o leitor vê.
    /// </summary>
    private static (string? Font, double? Size) ResolveEffectiveRunFormat(Paragraph p, StyleFormat styleFmt)
    {
        var fontWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sizeWeights = new Dictionary<double, int>();

        foreach (var run in p.Elements<Run>())
        {
            var text = run.InnerText;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var weight = text.Length;

            var font = run.RunProperties?.RunFonts?.Ascii?.Value ?? styleFmt.Font;
            if (!string.IsNullOrEmpty(font))
                fontWeights[font] = fontWeights.GetValueOrDefault(font) + weight;

            var size = ParseHalfPoints(run.RunProperties?.FontSize?.Val?.Value) ?? styleFmt.Size;
            if (size is { } s)
                sizeWeights[s] = sizeWeights.GetValueOrDefault(s) + weight;
        }

        var dominantFont = fontWeights.Count > 0
            ? fontWeights.OrderByDescending(kv => kv.Value).First().Key
            : styleFmt.Font;
        var dominantSize = sizeWeights.Count > 0
            ? sizeWeights.OrderByDescending(kv => kv.Value).First().Key
            : styleFmt.Size;

        return (dominantFont, dominantSize);
    }

    private static ExtractedDocumentProperties ExtractDocumentProperties(WordprocessingDocument doc)
    {
        var p = doc.PackageProperties;
        return new ExtractedDocumentProperties(
            Title: p.Title,
            Subject: p.Subject,
            Creator: p.Creator,
            LastModifiedBy: p.LastModifiedBy,
            Revision: p.Revision,
            Created: p.Created,
            Modified: p.Modified);
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
